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

        var catalogSettings = configuration.Get<CatalogSettings>() ?? new CatalogSettings();
        services.AddSingleton<IUriComposer>(new UriComposer(catalogSettings));

        services.AddScoped(typeof(IAppLogger<>), typeof(LoggerAdapter<>));
        services.AddTransient<IEmailSender, EmailSender>();

        services.AddSubscriptionServices(configuration);

        return services;
    }

    /// <summary>
    /// Registers the subscription feature. The billing provider is reached through one typed
    /// HttpClient whose BaseAddress comes from configuration, so the same build can target
    /// production, a dev/sandbox tenant, or a local mock server without a code change.
    /// </summary>
    public static IServiceCollection AddSubscriptionServices(this IServiceCollection services,
        IConfiguration configuration)
    {
        var maxioSection = configuration.GetSection(MaxioSettings.SECTION_NAME);

        services.Configure<MaxioSettings>(maxioSection);
        services.AddSingleton(maxioSection.Get<MaxioSettings>() ?? new MaxioSettings());
        services.AddSingleton(maxioSection.Get<SubscriptionSettings>() ?? new SubscriptionSettings());

        services.AddScoped<ISubscriptionService, SubscriptionService>();

        services.AddHttpClient<IBillingClient, MaxioBillingClient>((serviceProvider, httpClient) =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<MaxioSettings>>().Value;

            // Explicit Maxio:BaseUrl wins; otherwise the host is derived from the subdomain.
            httpClient.BaseAddress = MaxioBillingClient.CreateBaseAddress(settings);
            MaxioBillingClient.ConfigureAuthentication(httpClient, settings);
        });

        return services;
    }
}
