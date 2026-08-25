using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Maxio billing integration. Binds the "Maxio" configuration section
    /// (Maxio:ApiKey, Maxio:Subdomain, Maxio:ProductFamilyHandle, Maxio:BaseUrl) and wires
    /// the typed API client plus the domain orchestration service.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(MaxioSettings.CONFIG_NAME).Get<MaxioSettings>() ?? new MaxioSettings();
        services.AddSingleton(settings);

        services.AddTransient<MaxioRetryHandler>();
        services.AddHttpClient<IMaxioBillingClient, MaxioBillingClient>((_, client) =>
            MaxioBillingClient.ConfigureHttpClient(client, settings))
            .AddHttpMessageHandler(sp =>
                new MaxioRetryHandler(sp.GetRequiredService<ILogger<MaxioRetryHandler>>()));

        services.AddScoped<ISubscriptionBillingService, SubscriptionBillingService>();

        return services;
    }
}
