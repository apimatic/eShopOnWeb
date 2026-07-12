using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Web.Interfaces;
using Microsoft.eShopWeb.Web.Services;

namespace Microsoft.eShopWeb.Web.Configuration;

public static class ConfigureWebServices
{
    public static IServiceCollection AddWebServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Scans both Web (order/basket view-model handlers) and ApplicationCore (SubscriptionService's
        // notifications + the UC2 OrderPlacedUsageHandler) for MediatR handlers.
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssemblies(typeof(BasketViewModelService).Assembly, typeof(SubscriptionService).Assembly));
        services.AddScoped<IBasketViewModelService, BasketViewModelService>();
        services.AddScoped<CatalogViewModelService>();
        services.AddScoped<ICatalogItemViewModelService, CatalogItemViewModelService>();
        services.Configure<CatalogSettings>(configuration);
        services.AddScoped<ICatalogViewModelService, CachedCatalogViewModelService>();

        return services;
    }
}
