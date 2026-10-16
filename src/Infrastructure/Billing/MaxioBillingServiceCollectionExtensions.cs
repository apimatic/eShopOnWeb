using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public static class MaxioBillingServiceCollectionExtensions
{
    /// <summary>Name of the HttpClient the Maxio SDK client uses — kept off the app's shared default client.</summary>
    public const string HttpClientName = "MaxioAdvancedBilling";

    /// <summary>
    /// Registers subscription billing backed by Maxio Advanced Billing. Settings come from the <c>Maxio</c> section and
    /// are validated when the host starts, so a missing credential stops the app instead of failing the first request.
    /// The client is built once from those settings: a rotated API key takes effect on the next restart.
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<MaxioSettings>, MaxioSettingsValidator>();

        services.AddHttpClient(HttpClientName, client => client.Timeout = MaxioClientFactory.HttpClientTimeout)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client is a singleton holding one HttpClient; recycle connections so DNS changes are seen.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            });

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            var options = MaxioClientFactory.CreateOptions(settings,
                sp.GetRequiredService<ILoggerFactory>(),
                sp.GetRequiredService<TimeProvider>());
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new MaxioAdvancedBillingClient(httpClient, options);
        });

        services.AddMemoryCache();
        services.AddSingleton<ISubscriptionBillingGateway, MaxioBillingGateway>();
        services.AddScoped<ISubscriptionClaimStore, SubscriptionClaimStore>();
        services.AddScoped<ISubscriptionService, SubscriptionService>();
        return services;
    }
}
