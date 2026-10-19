using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

public static class MaxioServiceCollectionExtensions
{
    public const string HttpClientName = "MaxioAdvancedBilling";

    /// <summary>Per-attempt bound on waiting for Maxio's response headers.</summary>
    internal static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Registers the Maxio Advanced Billing client and <see cref="ISubscriptionBillingService"/>.
    /// The host refuses to start if a required <c>Maxio:*</c> setting is missing.
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.CONFIG_NAME))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<MaxioSettings>, MaxioSettingsValidator>();
        services.TryAddSingleton(TimeProvider.System);

        services.AddHttpClient(HttpClientName, c =>
            {
                // Backstop for one attempt; SDK retries and the service's call budget sit above it.
                c.Timeout = TimeSpan.FromSeconds(15);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client is a long-lived singleton; recycle pooled connections so DNS changes are seen.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            return new MaxioAdvancedBillingClient(httpClient, CreateClientOptions(settings, loggerFactory));
        });

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }

    /// <summary>
    /// Builds the SDK options once. The API key is captured here, so a rotated key takes effect on restart.
    /// </summary>
    public static MaxioAdvancedBillingClientOptions CreateClientOptions(MaxioSettings settings, ILoggerFactory loggerFactory)
    {
        var retryLogger = loggerFactory.CreateLogger("Maxio.Retry");
        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials
            {
                // Maxio Basic auth: the API key is the user name and the password is the literal "x".
                Username = settings.ApiKey!,
                Password = "x"
            },
            // Default retry policy never resends POSTs (customer/subscription creates); GETs are retried.
            Retry = RetryOptions.Default() with
            {
                Timeout = AttemptTimeout,
                OnRetry = attempt => retryLogger.LogWarning(
                    "Retrying Maxio call (attempt {Attempt}) after {Delay}: {Reason}",
                    attempt.AttemptNumber, attempt.Delay, attempt.Reason switch
                    {
                        RetryReason.Status s => $"HTTP {(int)s.StatusCode}",
                        RetryReason.Failure f => f.Exception.GetType().Name,
                        _ => "unknown"
                    })
            },
            // Customer create bodies carry personal data: never log bodies, and set the factory explicitly so
            // the MAXIOADVANCEDBILLINGCLIENT_LOG environment variable cannot switch body logging on.
            Logging = new LoggingOptions
            {
                LoggerFactory = loggerFactory,
                LogRequestBody = false,
                LogRequestHeaders = false,
                LogResponseHeaders = false
            }
        };

        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            options.Server.Production.Us.BaseUrl = settings.BaseUrl!;
            options.Server.Production.Eu.BaseUrl = settings.BaseUrl!;
        }
        else
        {
            options.Server.Production.Us.Site = settings.Subdomain!;
            options.Server.Production.Eu.Site = settings.Subdomain!;
        }

        return options;
    }
}
