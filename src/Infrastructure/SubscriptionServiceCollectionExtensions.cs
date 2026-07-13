using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure;

/// <summary>
/// Registers the subscription feature's use-case service and its single Maxio billing-client seam
/// (§2.1, §4.3 of the integration plan). Shared by both hosts (Web and PublicApi, neither of which
/// references the other) so the provider is still touched in exactly one Infrastructure class.
/// </summary>
public static class SubscriptionServiceCollectionExtensions
{
    public static IServiceCollection AddSubscriptionServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ISubscriptionService, SubscriptionService>();
        services.AddSingleton<IPlanChangePreviewCache, PlanChangePreviewCache>();
        services.Configure<MaxioSettings>(configuration.GetSection("Maxio"));

        // Typed client via IHttpClientFactory. The BaseAddress is resolved from configuration so the
        // SAME build can target prod / dev / a local mock — explicit Maxio:BaseUrl wins, else derive
        // from Subdomain (+ region). See §2.3. Do NOT hardcode the host.
        services.AddHttpClient<IBillingClient, MaxioBillingClient>((sp, http) =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            http.BaseAddress = settings.ResolveBaseUrl();
        });

        return services;
    }
}
