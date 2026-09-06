using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Registers Maxio Advanced Billing as the implementation of
/// <see cref="ISubscriptionBillingService"/>.
/// </summary>
public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// A named <see cref="HttpClient"/> rather than the default one the SDK's own registration helper takes:
    /// the default client is shared with every other unnamed consumer in the app, so a timeout or handler set
    /// for Maxio would change their behaviour too.
    /// </summary>
    public const string HttpClientName = "maxio-advanced-billing";

    public static IServiceCollection AddMaxioSubscriptionBilling(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.ConfigurationSection));

        services.AddMemoryCache();
        services.TryAddSingleton<IBillingOperationLock, InProcessBillingOperationLock>();
        services.TryAddTransient<MaxioCallScopeHandler>();
        services.TryAddTransient<MaxioRequestLoggingHandler>();

        services.AddHttpClient(HttpClientName, (provider, client) =>
            {
                var settings = provider.GetRequiredService<IOptions<MaxioSettings>>().Value;

                // Bounds a single attempt, not a whole call — the default is 100s, long enough for a hung
                // provider to pin a request thread for over a minute. The whole-call budget is a cancellation
                // token, applied in MaxioSubscriptionBillingService.
                client.Timeout = TimeSpan.FromSeconds(Math.Max(1, settings.AttemptTimeoutSeconds));
            })
            .AddHttpMessageHandler<MaxioCallScopeHandler>()
            .AddHttpMessageHandler<MaxioRequestLoggingHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client below is a singleton, so it holds one HttpClient for the process lifetime and
                // never sees the factory's handler rotation. Without this, a DNS change is cached forever.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.TryAddSingleton(provider =>
        {
            var settings = provider.GetRequiredService<IOptions<MaxioSettings>>().Value;
            var httpClient = provider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

            // The client is lightweight controller wrappers over the shared HTTP pipeline: build it once and
            // reuse it. It is built even when configuration is missing, so an unconfigured deployment still
            // starts and fails per-request with a clear message rather than refusing to boot.
            return new MaxioAdvancedBillingClient(httpClient, MaxioClientOptionsFactory.Create(settings));
        });

        services.TryAddSingleton<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }
}
