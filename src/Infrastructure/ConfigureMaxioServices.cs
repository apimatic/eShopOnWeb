using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure;

/// <summary>
/// Wires the eShopOnWeb Subscribe feature (§4.3): the typed <c>MaxioBillingClient</c> HttpClient, the
/// bound <see cref="MaxioSettings"/> options, and the ApplicationCore services that sit on top of them.
/// Called identically from both hosts (Web and PublicApi) so the provider is still touched in exactly one
/// place regardless of which host is composing it.
/// </summary>
public static class ConfigureMaxioServices
{
    public static IServiceCollection AddSubscriptionServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMemoryCache();
        services.Configure<MaxioSettings>(configuration.GetSection("Maxio"));

        // Typed HttpClient via IHttpClientFactory. MaxioBillingClient resolves the outbound target server
        // itself from MaxioSettings (explicit BaseUrl wins verbatim; otherwise derived from Subdomain +
        // Environment/region) using the Maxio SDK's own Server options — see MaxioBillingClient's
        // constructor. Do NOT set http.BaseAddress here: the SDK builds absolute request URLs itself and
        // never consults HttpClient.BaseAddress.
        services.AddHttpClient<IBillingClient, MaxioBillingClient>();

        services.AddSingleton<IPlanChangePreviewTokenService, PlanChangePreviewTokenService>();
        services.AddScoped<ISubscriptionService, SubscriptionService>();

        return services;
    }
}
