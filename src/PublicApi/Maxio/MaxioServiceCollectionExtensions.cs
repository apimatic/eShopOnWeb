using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public static class MaxioServiceCollectionExtensions
{
    public const string HttpClientName = "MaxioAdvancedBilling";

    /// <summary>
    /// Registers the Maxio Advanced Billing client and the billing service. The SDK client is a
    /// singleton over a named, factory-managed <see cref="HttpClient"/> so that its handler pipeline,
    /// timeouts and primary-handler connection lifetime are scoped to this integration, and so the
    /// <see cref="MaxioWriteGuardHandler"/> is attached exactly where the write-safety guarantee applies.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services)
    {
        services.AddSingleton<MaxioWriteGuardHandler>();

        services.AddHttpClient(HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(25);
        })
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2)
        })
        .AddHttpMessageHandler(sp => sp.GetRequiredService<MaxioWriteGuardHandler>());

        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var options = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;
            return CreateSdkClient(httpClient, options);
        });

        services.AddSingleton<MaxioBillingService>();

        return services;
    }

    private static MaxioAdvancedBillingClient CreateSdkClient(HttpClient httpClient, MaxioOptions settings)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey, Password = "x" },
            Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(10) }
        };

        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            options.Server.Production.Us.BaseUrl = settings.BaseUrl;
        }
        else
        {
            options.Server.Production.Us.Site = settings.Subdomain;
        }

        return new MaxioAdvancedBillingClient(httpClient, options);
    }
}
