using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Configuration;

/// <summary>
/// Composition-root wiring for the Maxio billing integration (§2.1/§4.3) - called from both hosts'
/// own composition roots (Web's <c>AddCoreServices</c> and PublicApi's <c>Program.cs</c>) so the
/// single Infrastructure client is registered identically everywhere it is used.
/// </summary>
public static class MaxioServiceCollectionExtensions
{
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioSettings>(configuration.GetSection("Maxio"));

        // Typed HttpClient via IHttpClientFactory (§4.3). The outbound base URL is resolved inside
        // MaxioBillingClient itself from MaxioSettings (explicit BaseUrl override, else Subdomain-derived) -
        // never hard-coded here.
        services.AddHttpClient<IBillingClient, MaxioBillingClient>();

        services.AddHostedService<MaxioSandboxValidationHostedService>();

        return services;
    }
}
