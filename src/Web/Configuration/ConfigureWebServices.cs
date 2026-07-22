using MediatR;
using Microsoft.eShopWeb.Web.Interfaces;
using Microsoft.eShopWeb.Web.Services;

namespace Microsoft.eShopWeb.Web.Configuration;

public static class ConfigureWebServices
{
    public static IServiceCollection AddWebServices(this IServiceCollection services, IConfiguration configuration)
    {
        // ApplicationCore is scanned alongside Web so the subscription notification handlers that
        // both hosts share are registered from one place (plan.md §2.5).
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssemblies(
                typeof(BasketViewModelService).Assembly,
                typeof(ApplicationCore.Interfaces.ISubscriptionService).Assembly));
        services.AddScoped<IBasketViewModelService, BasketViewModelService>();
        services.AddScoped<CatalogViewModelService>();
        services.AddScoped<ICatalogItemViewModelService, CatalogItemViewModelService>();
        services.Configure<CatalogSettings>(configuration);
        services.AddScoped<ICatalogViewModelService, CachedCatalogViewModelService>();

        return services;
    }
}
