using System;
using System.Linq;
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

/// <summary>
/// Wires Maxio Advanced Billing into the application's service container.
/// </summary>
public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Name of the dedicated <see cref="System.Net.Http.HttpClient"/> this integration uses. Registering
    /// over a named client keeps the timeout and the write-once handler off the shared default client, so
    /// nothing else in the application inherits them.
    /// </summary>
    public const string HttpClientName = "MaxioAdvancedBilling";

    /// <summary>
    /// Registers <see cref="ISubscriptionBillingService"/> from the <c>Maxio</c> configuration section.
    /// </summary>
    /// <remarks>
    /// Configuration is graded, not binary. A completely absent section registers a stand-in whose calls
    /// report "not configured", so a deployment that does not offer subscriptions still starts. A section
    /// that is present but incomplete throws here, at startup, because that is a deployment mistake worth
    /// failing on rather than discovering per request.
    /// </remarks>
    public static IServiceCollection AddMaxioSubscriptionBilling(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection(MaxioOptions.SectionName);
        services.Configure<MaxioOptions>(section);

        var options = section.Get<MaxioOptions>() ?? new MaxioOptions();

        if (IsAbsent(section))
        {
            services.AddSingleton<ISubscriptionBillingService, UnconfiguredSubscriptionBillingService>();
            return services;
        }

        var failures = options.Validate();
        if (failures.Count > 0)
        {
            throw new InvalidOperationException(
                "Maxio subscription billing is partially configured and cannot be used: "
                + string.Join(" ", failures)
                + $" Populate the '{MaxioOptions.SectionName}' section (user-secrets or environment variables), or remove it entirely to disable subscription billing.");
        }

        services.AddTransient<MaxioWriteOnceHandler>();
        services.AddTransient<MaxioRequestLoggingHandler>();

        var httpClientBuilder = services
            .AddHttpClient(HttpClientName, client =>
            {
                // Bounds a single attempt, not the whole call. The 100s default would let one hung
                // provider pin a request thread for over a minute.
                client.Timeout = TimeSpan.FromSeconds(options.AttemptTimeoutSeconds);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client below is a singleton holding one factory-created HttpClient, so the
                // factory's own handler rotation never reaches it; this is what keeps DNS fresh.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        if (options.LogRequests)
        {
            httpClientBuilder.AddHttpMessageHandler<MaxioRequestLoggingHandler>();
        }

        // Innermost handler: it must see each individual attempt the retry pipeline makes.
        httpClientBuilder.AddHttpMessageHandler<MaxioWriteOnceHandler>();

        services.AddSingleton<SubscriberEnrollmentLock>();
        services.AddSingleton(sp => BuildClient(sp));
        services.AddSingleton<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }

    private static MaxioAdvancedBillingClient BuildClient(IServiceProvider serviceProvider)
    {
        var options = serviceProvider.GetRequiredService<IOptions<MaxioOptions>>().Value;
        var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("MaxioAdvancedBillingClient");
        var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

        var clientOptions = new MaxioAdvancedBillingClientOptions
        {
            // The SDK exposes only US and EU hosting; a sandbox is an ordinary site on the same host,
            // selected by its subdomain rather than by a distinct environment.
            Environment = ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials
            {
                Username = options.ApiKey!,
                Password = "x"
            },
            Retry = RetryOptions.Default() with
            {
                MaxRetries = options.MaxRetries,
                Timeout = TimeSpan.FromSeconds(options.AttemptTimeoutSeconds),
                OnRetry = attempt => logger.LogWarning(
                    "Retrying Maxio request (attempt {AttemptNumber}) after {Delay}: {Reason}",
                    attempt.AttemptNumber,
                    attempt.Delay,
                    attempt.Reason)
            }
        };

        if (options.HasExplicitBaseUrl)
        {
            // The default base address is a "{site}" template expanded by string replacement, so a literal
            // URL passes through verbatim and the site value is not consulted at all.
            clientOptions.Server.Production.Us.BaseUrl = options.BaseUrl!;
        }
        else
        {
            // Left unset this silently defaults to the literal string "subdomain" and every call goes to
            // the wrong host, so it is set explicitly rather than relied upon.
            clientOptions.Server.Production.Us.Site = options.Subdomain!;
        }

        return new MaxioAdvancedBillingClient(httpClient, clientOptions);
    }

    private static bool IsAbsent(IConfigurationSection section) =>
        !section.Exists()
        || section.GetChildren().All(child => string.IsNullOrWhiteSpace(child.Value));
}
