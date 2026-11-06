using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public static class MaxioServiceCollectionExtensions
{
    private const string HTTP_CLIENT_NAME = "MaxioAdvancedBilling";

    /// <summary>Per-attempt bound for each Maxio call.</summary>
    public static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(10);

    public static IServiceCollection AddMaxioSubscriptions(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(MaxioOptions.CONFIG_NAME).Get<MaxioOptions>() ?? new MaxioOptions();
        Validate(settings);
        services.AddSingleton(settings);

        services.AddHttpClient(HTTP_CLIENT_NAME, c => c.Timeout = AttemptTimeout + TimeSpan.FromSeconds(5))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        // Options are built once here and captured by the singleton: a rotated key needs a restart.
        services.AddSingleton(sp =>
        {
            var options = new MaxioAdvancedBillingClientOptions
            {
                BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey!, Password = "x" },
                Retry = RetryOptions.Default() with { MaxRetries = 2, Timeout = AttemptTimeout },
                // Explicit factory: keeps MAXIO_ADVANCED_BILLING_LOG=trace from arming request-body logging.
                Logging = new LoggingOptions
                {
                    LoggerFactory = sp.GetRequiredService<ILoggerFactory>(),
                    LogRequestBody = false
                }
            };
            options.Server.Production.Us.Site = settings.Subdomain!;
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Production.Us.BaseUrl = settings.BaseUrl!;
            }
            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HTTP_CLIENT_NAME);
            return new MaxioAdvancedBillingClient(http, options);
        });

        services.AddScoped<MaxioSubscriptionService>();
        return services;
    }

    private static void Validate(MaxioOptions s)
    {
        if (string.IsNullOrWhiteSpace(s.ApiKey))
            throw new InvalidOperationException("Maxio:ApiKey is not configured. Set it via user-secrets or the Maxio__ApiKey environment variable.");
        if (string.IsNullOrWhiteSpace(s.Subdomain))
            throw new InvalidOperationException("Maxio:Subdomain is not configured.");
        if (string.IsNullOrWhiteSpace(s.ProductFamilyHandle))
            throw new InvalidOperationException("Maxio:ProductFamilyHandle is not configured.");
    }
}
