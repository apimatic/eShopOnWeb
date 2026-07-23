using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Logging;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Configuration;

/// <summary>
/// Registers the subscription feature's services. Both hosts call this so the storefront and the
/// PublicApi share one billing client and one set of settings.
/// </summary>
public static class ConfigureBillingServices
{
    public static IServiceCollection AddBillingServices(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.CONFIG_SECTION));

        // The billing client logs through eShopOnWeb's own abstraction. Both hosts already register
        // this; TryAdd lets a standalone caller (such as the seed tool) work without duplicating it.
        services.TryAddScoped(typeof(IAppLogger<>), typeof(LoggerAdapter<>));

        // The outbound target is resolved from configuration, so the same build can be pointed at
        // production, a sandbox tenant, or a local mock server without a code change. An explicit
        // Maxio:BaseUrl always wins over the subdomain-derived host.
        services.AddHttpClient<IBillingClient, MaxioBillingClient>((serviceProvider, httpClient) =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<MaxioSettings>>().Value;
            httpClient.BaseAddress = settings.ResolveBaseUrl();
        });

        // Caches the "is the usage component really metered?" check for the process lifetime.
        services.AddSingleton<IMeteredComponentValidator, MeteredComponentValidator>();

        services.AddScoped<ISubscriptionService, SubscriptionService>();

        return services;
    }
}
