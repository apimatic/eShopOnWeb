using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Configuration;

public static class MaxioServiceCollectionExtensions
{
    // Binds MaxioSettings from the "Maxio" configuration section and registers IBillingClient
    // as a typed HttpClient (IHttpClientFactory) backed by MaxioBillingClient — the single
    // Infrastructure class that talks to the billing provider.
    public static IServiceCollection AddMaxioBillingClient(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<MaxioSettings>(configuration.GetSection("Maxio"));
        services.AddHttpClient<IBillingClient, MaxioBillingClient>();

        return services;
    }
}
