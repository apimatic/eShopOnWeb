using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public static class MaxioServiceCollectionExtensions
{
    private const string HTTP_CLIENT_NAME = "Maxio";

    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        // Throws at startup when a setting is missing or blank.
        var settings = MaxioSettings.Load(configuration);

        services.AddHttpClient(HTTP_CLIENT_NAME, c => c.Timeout = TimeSpan.FromSeconds(20))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        // Options are built once and captured by the singleton: a rotated key needs a restart.
        services.AddSingleton(sp =>
        {
            var useEu = string.Equals(settings.Environment!.Trim(), "EU", StringComparison.OrdinalIgnoreCase);
            var options = new MaxioAdvancedBillingClientOptions
            {
                Environment = useEu ? ServerEnvironment.Eu : ServerEnvironment.Us,
                BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey!.Trim(), Password = "x" },
                Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(20), MaxRetries = 2 },
                Logging = new LoggingOptions
                {
                    LoggerFactory = sp.GetRequiredService<ILoggerFactory>(),
                    LogRequestBody = false
                }
            };

            var server = options.Server.Production;
            if (useEu)
            {
                server.Eu.Site = settings.Subdomain!.Trim();
                if (!string.IsNullOrWhiteSpace(settings.BaseUrl)) server.Eu.BaseUrl = settings.BaseUrl;
            }
            else
            {
                server.Us.Site = settings.Subdomain!.Trim();
                if (!string.IsNullOrWhiteSpace(settings.BaseUrl)) server.Us.BaseUrl = settings.BaseUrl;
            }

            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HTTP_CLIENT_NAME);
            return new MaxioAdvancedBillingClient(http, options);
        });

        services.AddScoped<BillingClaims>();
        services.AddScoped<BillingService>();
        return services;
    }
}
