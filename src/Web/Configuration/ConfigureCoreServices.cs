using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Data.Queries;
using Microsoft.eShopWeb.Infrastructure.Logging;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Web.Configuration;

public static class ConfigureCoreServices
{
    public static IServiceCollection AddCoreServices(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped(typeof(IReadRepository<>), typeof(EfRepository<>));
        services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));

        services.AddScoped<IBasketService, BasketService>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<IBasketQueryService, BasketQueryService>();
        services.AddScoped<ISubscriptionService, SubscriptionService>();

        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.CONFIG_NAME));

        // Typed client via IHttpClientFactory. The BaseAddress comes from configuration so the same
        // build can target production, a dev/sandbox tenant, or a local mock — an explicit
        // Maxio:BaseUrl wins, otherwise the host is derived from the subdomain (plan.md §2.3).
        services.AddHttpClient<IBillingClient, MaxioBillingClient>((sp, http) =>
        {
            var maxioSettings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            http.BaseAddress = new Uri(maxioSettings.ResolveBaseUrl());
        });

        var catalogSettings = configuration.Get<CatalogSettings>() ?? new CatalogSettings();
        services.AddSingleton<IUriComposer>(new UriComposer(catalogSettings));

        services.AddScoped(typeof(IAppLogger<>), typeof(LoggerAdapter<>));
        services.AddTransient<IEmailSender, EmailSender>();

        return services;
    }
}
