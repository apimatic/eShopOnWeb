using System;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.SquareIntegration;

public static class SquareServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Square integration. Settings bind from the <c>Square:</c> section and are validated
    /// at startup — the host refuses to start when a required one is missing.
    /// </summary>
    public static IServiceCollection AddSquareIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SquareSettings>()
            .Bind(configuration.GetSection(SquareSettings.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<SquareSettings>, SquareSettingsValidator>());

        services.TryAddSingleton(TimeProvider.System);
        services.AddMemoryCache();
        services.AddDataProtection();

        services.AddHttpClient(SquareClientFactory.HttpClientName, client =>
            {
                // Per-attempt backstop; the SDK's own per-attempt timeout (15 s) fires first.
                client.Timeout = TimeSpan.FromSeconds(20);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client is long-lived; recycle connections so DNS changes are picked up.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            });

        services.AddSingleton<SquareClientFactory>();
        services.AddSingleton<SquareOAuthApi>();
        services.AddSingleton<SquareTokenSource>();
        services.AddSingleton<SquareClientHolder>();
        services.AddSingleton<SquareLeaseStore>();
        services.AddSingleton<SquareMerchantContextProvider>();
        services.AddSingleton<SquareCatalogLocator>();
        services.AddSingleton<SquareGiftMessageField>();
        services.AddSingleton<SquareOrderPublisher>();

        services.AddScoped<SquareOAuthService>();
        services.AddScoped<SquareCatalogSyncService>();
        services.AddScoped<SquarePhotoService>();
        services.AddScoped<SquareOrderService>();
        return services;
    }
}
