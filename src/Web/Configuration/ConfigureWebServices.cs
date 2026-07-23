using MediatR;
using Microsoft.eShopWeb.Web.Interfaces;
using Microsoft.eShopWeb.Web.Services;

namespace Microsoft.eShopWeb.Web.Configuration;

public static class ConfigureWebServices
{
    public static IServiceCollection AddWebServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Scan the ApplicationCore assembly as well so the subscription integration events and any
        // handlers that live alongside the domain are discovered.
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssemblies(
                typeof(BasketViewModelService).Assembly,
                typeof(ApplicationCore.Services.SubscriptionService).Assembly));
        services.AddScoped<IBasketViewModelService, BasketViewModelService>();
        services.AddScoped<CatalogViewModelService>();
        services.AddScoped<ICatalogItemViewModelService, CatalogItemViewModelService>();
        services.Configure<CatalogSettings>(configuration);
        services.AddScoped<ICatalogViewModelService, CachedCatalogViewModelService>();

        return services;
    }
}
