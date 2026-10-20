using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public static class MaxioServiceCollectionExtensions
{
    public const string HttpClientName = "MaxioAdvancedBilling";

    /// <summary>Per-attempt bound; the whole-call budget is enforced in <see cref="SubscriptionService"/>.</summary>
    public static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Registers the billing use cases, the Maxio gateway and the SDK client.
    /// Throws at startup (naming the key, never the value) when configuration is missing or invalid.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(MaxioSettings.SectionName).Get<MaxioSettings>() ?? new MaxioSettings();
        var problems = settings.Validate();
        if (problems.Count > 0)
        {
            throw new InvalidOperationException("Invalid Maxio configuration: " + string.Join("; ", problems));
        }
        var environment = MaxioSettings.ParseEnvironment(configuration["MAXIO_ENVIRONMENT"]);

        services.AddSingleton(settings);

        services.AddHttpClient(HttpClientName, c => c.Timeout = AttemptTimeout)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        // Options are built once, here, and captured by the singleton client: a rotated key needs a restart.
        services.AddSingleton(sp =>
        {
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            var retryLogger = loggerFactory.CreateLogger("Maxio.Retry");
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

            var options = new MaxioAdvancedBillingClientOptions
            {
                Environment = environment,
                BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey!, Password = "x" },
                Retry = RetryOptions.Default() with
                {
                    Timeout = AttemptTimeout,
                    MaxRetries = 2,
                    OnRetry = attempt => retryLogger.LogWarning("Maxio call retry #{Attempt} in {DelayMs} ms ({Reason})",
                        attempt.AttemptNumber, (int)attempt.Delay.TotalMilliseconds,
                        attempt.Reason is RetryReason.Status s ? $"status {(int)s.StatusCode}" : "transport failure")
                },
                // Explicit factory: disables the MAXIOADVANCEDBILLINGCLIENT_LOG trace switch; bodies carry customer PII.
                Logging = new LoggingOptions
                {
                    LoggerFactory = loggerFactory,
                    LogRequestBody = false,
                    LogRequestHeaders = false,
                    LogResponseHeaders = false
                }
            };

            if (environment == ServerEnvironment.Eu)
            {
                options.Server.Production.Eu.Site = settings.Subdomain!;
                if (!string.IsNullOrWhiteSpace(settings.BaseUrl)) options.Server.Production.Eu.BaseUrl = settings.BaseUrl!;
            }
            else
            {
                options.Server.Production.Us.Site = settings.Subdomain!;
                if (!string.IsNullOrWhiteSpace(settings.BaseUrl)) options.Server.Production.Us.BaseUrl = settings.BaseUrl!;
            }

            return new MaxioAdvancedBillingClient(httpClient, options);
        });

        services.AddSingleton<IBillingGateway, MaxioBillingGateway>();
        services.AddScoped<IBillingClaimStore, BillingClaimStore>();
        services.AddScoped<SubscriptionService>();
        return services;
    }
}
