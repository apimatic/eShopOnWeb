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
    /// Registers the subscription feature: the use-case service and the single billing-provider client
    /// behind its provider-agnostic interface (plan.md §4.3).
    /// </summary>
    /// <remarks>
    /// The outbound target is resolved from configuration — an explicit <c>Maxio:BaseUrl</c> wins over the
    /// subdomain-derived host — so the same build can be pointed at production, a dev/sandbox tenant, or a
    /// local mock server without a code change (plan.md §2.3). The host is never hardcoded here.
    /// </remarks>
    public static IServiceCollection AddSubscriptionServices(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.SectionName));
        services.AddScoped<ISubscriptionService, SubscriptionService>();

        services.AddHttpClient<IBillingClient, MaxioBillingClient>((serviceProvider, httpClient) =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<MaxioSettings>>().Value;
            httpClient.BaseAddress = new Uri(settings.ResolveBaseUrl());
        });

        return services;
    }
}
