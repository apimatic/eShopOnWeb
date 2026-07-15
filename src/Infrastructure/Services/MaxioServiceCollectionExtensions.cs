using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Configuration;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Wires the Maxio Advanced Billing SDK client and <see cref="IBillingClient"/> into DI. This is the only
/// place the SDK's own registration extension is called and the only place the outbound base URL is
/// resolved (plan.md §2.2/§2.3/§4.3) — composition roots (Web, PublicApi) call only
/// <see cref="AddMaxioBilling"/>, never the SDK directly.
/// </summary>
public static class MaxioServiceCollectionExtensions
{
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection("Maxio").Get<MaxioSettings>() ?? new MaxioSettings();
        services.Configure<MaxioSettings>(configuration.GetSection("Maxio"));

        services.AddMaxioAdvancedBillingClient(options =>
        {
            options.BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey, Password = "x" };
            options.Environment = settings.IsEuRegion ? ServerEnvironment.Eu : ServerEnvironment.Us;

            // Explicit Maxio:BaseUrl always wins verbatim; otherwise this derives the host from the
            // subdomain + region. Either way, the resolved value is assigned as the literal BaseUrl — the
            // single knob that lets the identical build target production, a dev/sandbox tenant, or a
            // local mock server purely through configuration (plan.md §2.3).
            var baseUrl = settings.ResolveBaseUrl();
            if (settings.IsEuRegion)
            {
                options.Server.Production.Eu.BaseUrl = baseUrl;
            }
            else
            {
                options.Server.Production.Us.BaseUrl = baseUrl;
            }
        });

        services.AddScoped<IBillingClient, MaxioBillingClient>();

        return services;
    }
}
