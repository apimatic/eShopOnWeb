using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Walmart;

public static class WalmartServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Walmart Marketplace inventory integration: settings binding from the
    /// <c>Walmart:</c> section, the OAuth token provider, the bearer-auth delegating handler, and
    /// the typed <see cref="IWalmartInventoryService"/> HTTP client. Base addresses are resolved
    /// from the contracts' server URLs and the optional <c>Walmart:BaseUrl</c> override.
    /// </summary>
    public static IServiceCollection AddWalmartInventoryIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<WalmartSettings>()
            .Bind(configuration.GetSection(WalmartSettings.SectionName));

        services.AddMemoryCache();

        services.AddSingleton<IWalmartTokenProvider, WalmartTokenProvider>();
        services.AddTransient<WalmartAuthenticationHandler>();

        // Token client: talks to the Authorization API base (.../seller/). No bearer handler — this
        // client is how the bearer token is obtained in the first place.
        services.AddHttpClient(WalmartTokenProvider.HttpClientName, (sp, client) =>
        {
            var settings = sp.GetRequiredService<IOptions<WalmartSettings>>().Value;
            client.BaseAddress = WalmartEndpoints.ResolveAuthBase(settings.BaseUrl);
        });

        // Inventory client: talks to the Inventory API base (.../seller/v1/) with the bearer token
        // attached by the auth handler.
        services.AddHttpClient<IWalmartInventoryService, WalmartInventoryService>((sp, client) =>
        {
            var settings = sp.GetRequiredService<IOptions<WalmartSettings>>().Value;
            client.BaseAddress = WalmartEndpoints.ResolveInventoryBase(settings.BaseUrl);
        })
        .AddHttpMessageHandler<WalmartAuthenticationHandler>();

        return services;
    }
}
