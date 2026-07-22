using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Data.Queries;
using Microsoft.eShopWeb.Infrastructure.Logging;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.eShopWeb.Infrastructure.Services.Maxio;
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

        var catalogSettings = configuration.Get<CatalogSettings>() ?? new CatalogSettings();
        services.AddSingleton<IUriComposer>(new UriComposer(catalogSettings));

        services.AddScoped(typeof(IAppLogger<>), typeof(LoggerAdapter<>));
        services.AddTransient<IEmailSender, EmailSender>();

        services.AddBillingProvider(configuration);

        return services;
    }

    /// <summary>
    /// Registers the single provider seam as a typed <see cref="HttpClient"/>. The outbound target
    /// comes from configuration — an explicit Maxio:BaseUrl wins, otherwise the host is derived
    /// from the subdomain — so the same build can be pointed at production, a dev/sandbox tenant
    /// or a local mock server without a code change (plan §2.3/§4.3). Do NOT hardcode the host.
    /// </summary>
    public static IServiceCollection AddBillingProvider(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.ConfigurationSection));
        services.AddTransient<MaxioTransientFaultHandler>();

        services.AddHttpClient<IBillingClient, MaxioBillingClient>((sp, http) =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            http.BaseAddress = new Uri(settings.ResolveBaseUrl());
            http.Timeout = TimeSpan.FromSeconds(Math.Max(1, settings.TimeoutSeconds));
        })
        .AddHttpMessageHandler<MaxioTransientFaultHandler>();

        return services;
    }
}
