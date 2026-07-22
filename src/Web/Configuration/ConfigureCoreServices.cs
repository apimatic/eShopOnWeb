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

        // Maxio billing integration. The typed HttpClient's BaseAddress is resolved from
        // configuration so the SAME build can target prod / dev / a local mock — explicit
        // Maxio:BaseUrl wins, else derive from Subdomain (+ region). See §2.3 / §4.3.
        services.Configure<MaxioSettings>(configuration.GetSection("Maxio"));
        services.AddHttpClient<IBillingClient, MaxioBillingClient>((sp, http) =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            MaxioHttpClientConfigurator.Configure(http, settings);
        });

        var catalogSettings = configuration.Get<CatalogSettings>() ?? new CatalogSettings();
        services.AddSingleton<IUriComposer>(new UriComposer(catalogSettings));

        services.AddScoped(typeof(IAppLogger<>), typeof(LoggerAdapter<>));
        services.AddTransient<IEmailSender, EmailSender>();

        return services;
    }
}
