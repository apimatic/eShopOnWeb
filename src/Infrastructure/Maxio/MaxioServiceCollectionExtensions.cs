using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    private const string MaxioHttpClientName = "MaxioAdvancedBilling";

    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioOptions>(configuration.GetSection("Maxio"));
        services.AddMemoryCache();

        services.AddHttpClient(MaxioHttpClientName, c =>
            {
                // Bounds one attempt; the per-request CancellationToken bounds the whole call (see MaxioBillingService).
                c.Timeout = TimeSpan.FromSeconds(15);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var maxioOptions = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(MaxioHttpClientName);

            var clientOptions = new MaxioAdvancedBillingClientOptions
            {
                Environment = ServerEnvironment.Us,
                Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(10) },
                BasicAuth = new BasicAuthCredentials
                {
                    Username = maxioOptions.ApiKey,
                    Password = "x"
                }
            };
            clientOptions.Server.Production.Us.Site = maxioOptions.Subdomain;
            if (!string.IsNullOrWhiteSpace(maxioOptions.BaseUrl))
            {
                clientOptions.Server.Production.Us.BaseUrl = maxioOptions.BaseUrl;
            }

            return new MaxioAdvancedBillingClient(httpClient, clientOptions);
        });

        services.AddScoped<IMaxioBillingService, MaxioBillingService>();

        return services;
    }
}
