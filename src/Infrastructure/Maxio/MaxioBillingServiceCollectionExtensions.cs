using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioBillingServiceCollectionExtensions
{
    /// <summary>
    /// Name of the dedicated <see cref="HttpClient"/> the Maxio client is built over. The SDK's own DI
    /// extension resolves the default, unnamed factory client instead — which would put this
    /// integration's timeout and message handlers on the pipeline every other unnamed
    /// <c>CreateClient()</c> consumer in the app shares. Registering the client here keeps them local.
    /// </summary>
    public const string HttpClientName = "maxio-advanced-billing";

    /// <summary>
    /// Registers Maxio Advanced Billing as the subscription billing system of record.
    /// </summary>
    /// <remarks>
    /// Binds the <c>Maxio</c> configuration section. Credentials are expected from user-secrets or the
    /// environment; nothing here has a hard-coded value, so the same build runs against a different
    /// Maxio site and a different catalog by changing configuration alone.
    /// </remarks>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(MaxioSettings.SectionName);
        services.Configure<MaxioSettings>(section);

        var settings = section.Get<MaxioSettings>() ?? new MaxioSettings();

        services.AddTransient<MaxioLoggingHandler>();
        services.AddTransient<MaxioWriteGuardHandler>();

        var httpClientBuilder = services
            .AddHttpClient(HttpClientName, client =>
            {
                // Bounds a single attempt, not the whole call — but its expiry throws a cancellation the
                // SDK's retry pipeline does not retry, so it is what actually caps a hung provider. The
                // whole-call budget is applied by MaxioBillingService.
                client.Timeout = TimeSpan.FromSeconds(Math.Max(1, settings.HttpTimeoutSeconds));
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client below is a singleton, so it holds one HttpClient for the process
                // lifetime and never sees the factory rotate handlers. Without this, a DNS change on the
                // Maxio site would be cached indefinitely.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        if (settings.LogRequests)
        {
            httpClientBuilder.AddHttpMessageHandler<MaxioLoggingHandler>();
        }

        // Innermost, so it counts requests that actually reach the network.
        httpClientBuilder.AddHttpMessageHandler<MaxioWriteGuardHandler>();

        services.AddSingleton(serviceProvider =>
        {
            var current = serviceProvider.GetRequiredService<IOptions<MaxioSettings>>().Value;
            var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var logger = serviceProvider.GetRequiredService<ILogger<MaxioAdvancedBillingClient>>();

            return CreateClient(httpClient, current, logger);
        });

        services.AddSingleton<ISubscriptionBillingService, MaxioBillingService>();

        return services;
    }

    /// <summary>
    /// Reports the state of the Maxio configuration at startup, so a deployment missing credentials says
    /// so once on boot rather than only on the first customer request.
    /// </summary>
    /// <remarks>
    /// A missing API key is a warning, not a fatal error: the subscription endpoints answer 503 and the
    /// rest of eShopOnWeb — which does not depend on billing — keeps working. Configuration that is
    /// present but invalid is a deployment mistake and throws.
    /// </remarks>
    public static void ValidateMaxioBilling(IServiceProvider serviceProvider, ILogger logger)
    {
        var settings = serviceProvider.GetRequiredService<IOptions<MaxioSettings>>().Value;
        var problems = settings.Validate();

        if (problems.Count == 0)
        {
            logger.LogInformation(
                "Maxio billing configured for product family '{ProductFamilyHandle}'.",
                settings.ProductFamilyHandle);
            return;
        }

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            logger.LogWarning(
                "Maxio billing is not configured; the subscription endpoints will answer 503. {Problems}",
                string.Join(" ", problems));
            return;
        }

        throw new InvalidOperationException(
            "Maxio billing is configured incorrectly. " + string.Join(" ", problems));
    }

    private static MaxioAdvancedBillingClient CreateClient(
        HttpClient httpClient,
        MaxioSettings settings,
        ILogger logger)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            // The SDK models only US and EU hosting here. A Maxio sandbox is a site, not an environment,
            // so it is reached through the subdomain (or Maxio:BaseUrl) rather than this value.
            Environment = ServerEnvironment.Us,
            Retry = RetryOptions.Default() with
            {
                MaxRetries = Math.Max(1, settings.MaxRetries),
                Timeout = TimeSpan.FromSeconds(Math.Max(1, settings.RetryTimeoutSeconds)),
                OnRetry = attempt => logger.LogWarning(
                    "Retrying Maxio request (attempt {AttemptNumber}) after {Delay} — {Reason}.",
                    attempt.AttemptNumber, attempt.Delay, attempt.Reason)
            }
        };

        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            // Maxio takes the API key as the Basic user name with a fixed placeholder password. Leaving
            // this unset would produce a client that constructs happily and sends no Authorization
            // header at all, so the failure would only surface as a runtime 401.
            options.BasicAuth = new BasicAuthCredentials
            {
                Username = settings.ApiKey!,
                Password = "x"
            };
        }

        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            // BaseUrl is a template in which only a literal {site} token is substituted, so a plain URL
            // is used exactly as given. Set on the node matching Environment — nothing else is read.
            options.Server.Production.Us.BaseUrl = settings.BaseUrl!;

            logger.LogInformation("Maxio API base address overridden by configuration.");
        }
        else if (!string.IsNullOrWhiteSpace(settings.Subdomain))
        {
            options.Server.Production.Us.Site = settings.Subdomain!;
        }

        return new MaxioAdvancedBillingClient(httpClient, options);
    }
}
