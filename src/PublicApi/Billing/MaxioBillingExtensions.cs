using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public static class MaxioBillingExtensions
{
    /// <summary>
    /// Named HttpClient backing the Maxio SDK client — keeps its handler pipeline,
    /// timeout and connection lifetime off the shared default client.
    /// </summary>
    public const string HttpClientName = "Maxio";

    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioOptions>(configuration.GetSection(MaxioOptions.SectionName));

        services.AddHttpClient(HttpClientName, client =>
            {
                // Bounds one attempt (the SDK retry pipeline sits above SendAsync, so every
                // attempt gets a fresh full timeout). The whole-call budget lives in the service.
                client.Timeout = TimeSpan.FromSeconds(10);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client below is a singleton: keep DNS fresh behind it.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var maxioOptions = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

            var clientOptions = new MaxioAdvancedBillingClientOptions
            {
                Environment = ServerEnvironment.Us,
                BasicAuth = new BasicAuthCredentials
                {
                    Username = maxioOptions.ApiKey ?? string.Empty,
                    Password = "x"
                },
                Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(10) }
            };

            clientOptions.Server.Production.Us.Site = maxioOptions.Subdomain ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(maxioOptions.BaseUrl))
            {
                // Optional override: used verbatim as the API base address.
                clientOptions.Server.Production.Us.BaseUrl = maxioOptions.BaseUrl;
            }

            return new MaxioAdvancedBillingClient(httpClient, clientOptions);
        });

        services.AddSingleton<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }
}
