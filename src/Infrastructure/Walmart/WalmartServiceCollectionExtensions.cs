using Microsoft.eShopWeb.ApplicationCore.Walmart;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Walmart;

public static class WalmartServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Walmart Marketplace integration: options bound from the <c>Walmart:</c>
    /// section, the OAuth token provider and auth handler, and the typed inventory client built
    /// against the Walmart OpenAPI contract.
    /// </summary>
    public static IServiceCollection AddWalmartIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<WalmartOptions>(configuration.GetSection(WalmartOptions.SectionName));

        services.AddSingleton<WalmartEndpointResolver>();
        services.AddSingleton<WalmartTokenProvider>();
        services.AddTransient<WalmartAuthenticationHandler>();

        // Plain client for the token call — no auth handler (it is what obtains the token).
        services.AddHttpClient(WalmartTokenProvider.HttpClientName);

        // Typed inventory client; every call carries the bearer token via the auth handler.
        services.AddHttpClient<IWalmartInventoryClient, WalmartInventoryClient>()
            .AddHttpMessageHandler<WalmartAuthenticationHandler>();

        return services;
    }
}
