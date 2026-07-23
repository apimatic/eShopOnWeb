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
    /// <summary>Caps how long any single outbound Maxio request may take.</summary>
    private static readonly TimeSpan MaxioRequestTimeout = TimeSpan.FromSeconds(15);

    public static IServiceCollection AddCoreServices(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped(typeof(IReadRepository<>), typeof(EfRepository<>));
        services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));

        services.AddScoped<IBasketService, BasketService>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<IBasketQueryService, BasketQueryService>();
        services.AddScoped<ISubscriptionService, SubscriptionService>();

        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.ConfigurationSectionName));

        // Resolved handle-to-id lookups are shared process-wide rather than repeated per request.
        services.AddSingleton<MaxioCatalogCache>();

        // Typed client via IHttpClientFactory. The outbound target is resolved from configuration so
        // the same build can be pointed at production, a dev/sandbox tenant, or a local mock server:
        // an explicit Maxio:BaseUrl wins, otherwise the host is derived from Maxio:Subdomain (§2.3).
        services.AddHttpClient<IBillingClient, MaxioBillingClient>((sp, http) =>
        {
            var maxioSettings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            if (maxioSettings.TryResolveBaseUrl(out var baseUrl))
            {
                http.BaseAddress = new Uri(baseUrl!);
            }

            // Checkout raises the usage notification in-process, so a stalled provider must not hold
            // an order request open for the 100-second default.
            http.Timeout = MaxioRequestTimeout;
        });

        var catalogSettings = configuration.Get<CatalogSettings>() ?? new CatalogSettings();
        services.AddSingleton<IUriComposer>(new UriComposer(catalogSettings));

        services.AddScoped(typeof(IAppLogger<>), typeof(LoggerAdapter<>));
        services.AddTransient<IEmailSender, EmailSender>();

        return services;
    }
}
