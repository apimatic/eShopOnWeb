using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public static class MaxioServiceCollectionExtensions
{
    private const string HttpClientName = "Maxio";

    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioOptions>()
            .Bind(configuration.GetSection(MaxioOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.ApiKey), "Maxio:ApiKey is required (set the MAXIO_API_KEY environment variable or user-secret).")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Subdomain), "Maxio:Subdomain is required (set the MAXIO_SITE_SUBDOMAIN environment variable or user-secret).")
            .Validate(o => !string.IsNullOrWhiteSpace(o.ProductFamilyHandle), "Maxio:ProductFamilyHandle is required (set the MAXIO_DEFAULT_PRODUCT_FAMILY environment variable or user-secret).")
            .ValidateOnStart();

        // Named client keeps this pipeline (timeout, handler lifetime) off the shared default client.
        services.AddHttpClient(HttpClientName, c =>
            {
                // Bounds one attempt; the SDK's retry pipeline sits above SendAsync.
                c.Timeout = TimeSpan.FromSeconds(30);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client below is a singleton; keep DNS fresh behind it.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var maxioOptions = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

            var options = new MaxioAdvancedBillingClientOptions
            {
                Environment = ServerEnvironment.Us,
                BasicAuth = new BasicAuthCredentials
                {
                    Username = maxioOptions.ApiKey,
                    Password = "x"
                },
                Retry = RetryOptions.Default() with
                {
                    Timeout = TimeSpan.FromSeconds(30)
                }
            };

            if (!string.IsNullOrWhiteSpace(maxioOptions.BaseUrl))
            {
                options.Server.Production.Us.BaseUrl = maxioOptions.BaseUrl;
            }
            else
            {
                options.Server.Production.Us.Site = maxioOptions.Subdomain;
            }

            return new MaxioAdvancedBillingClient(httpClient, options);
        });

        services.AddScoped<IMaxioBillingService, MaxioBillingService>();

        return services;
    }
}
