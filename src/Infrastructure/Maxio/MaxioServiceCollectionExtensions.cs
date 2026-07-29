using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Registers the Maxio Advanced Billing integration: settings binding, the SDK client over a
/// dedicated <see cref="System.Net.Http.IHttpClientFactory"/> client, and the subscription service.
/// Registration never fails on missing configuration — the app boots regardless, and a call made
/// without valid credentials fails at call time with a translated error (so unrelated hosts/tests
/// that never touch billing are unaffected).
/// </summary>
public static class MaxioServiceCollectionExtensions
{
    public const string HttpClientName = "Maxio";

    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.CONFIG_NAME));
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<MaxioSettings>>().Value);

        services.AddMemoryCache();

        // The SDK client is long-lived (a singleton, below), so it holds one HttpClient for the
        // process. Bound the primary handler's connection lifetime so DNS changes are eventually
        // picked up even though IHttpClientFactory handler rotation does not reach a captured client.
        services.AddHttpClient(HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<MaxioSettings>();
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var options = MaxioClientOptionsFactory.Create(settings);
            return new MaxioAdvancedBillingClient(httpClient, options);
        });

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }
}
