using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>Name of the HttpClient reserved for the Maxio SDK (kept off the shared default client).</summary>
    public const string HttpClientName = "MaxioAdvancedBilling";

    /// <summary>
    /// Registers the Maxio integration. Settings are bound from the <c>Maxio</c> section and validated when the
    /// host starts (<c>ValidateOnStart</c>), so a missing key stops the host instead of surfacing as a 401.
    /// The SDK client is a singleton that captures the settings at first resolution: rotating
    /// <c>Maxio:ApiKey</c> takes effect on the next process restart.
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioOptions>()
            .Bind(configuration.GetSection(MaxioOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<MaxioOptions>, MaxioOptionsValidator>();

        services.TryAddSingleton(TimeProvider.System);
        services.AddMemoryCache();

        services.AddHttpClient(HttpClientName, client =>
            {
                // Per-attempt backstop above the SDK's own per-attempt timeout; the whole-call bound is the
                // per-request budget token (MaxioCallBudget).
                client.Timeout = MaxioCallBudget.HttpClientTimeout;
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client below is a singleton holding this HttpClient: recycle pooled connections so DNS
                // changes are picked up.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var options = MaxioClientOptionsFactory.Create(settings, sp.GetRequiredService<ILoggerFactory>(), sp.GetService<TimeProvider>());
            return new MaxioAdvancedBillingClient(httpClient, options);
        });

        services.AddSingleton<MaxioGateway>();
        services.AddScoped<MaxioPlanCatalog>();
        services.AddScoped<MaxioCustomerService>();
        services.AddScoped<MaxioSubscriptionService>();
        return services;
    }
}
