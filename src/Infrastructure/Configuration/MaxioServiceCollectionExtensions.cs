using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Configuration;

/// <summary>
/// Composition-root wiring for the Maxio integration (§2.1/§4.3) — the only place the SDK client
/// is constructed and the only place the outbound target server is resolved (§2.3): an explicit
/// <c>Maxio:BaseUrl</c> always wins verbatim; otherwise the host is derived from
/// <c>Maxio:Subdomain</c> and the <c>Maxio:Environment</c> region.
/// </summary>
public static class MaxioServiceCollectionExtensions
{
    public static IServiceCollection AddMaxioBillingClient(this IServiceCollection services, IConfiguration configuration)
    {
        var maxioSection = configuration.GetSection("Maxio");
        services.Configure<MaxioSettings>(maxioSection);
        var settings = maxioSection.Get<MaxioSettings>() ?? new MaxioSettings();

        services.AddMaxioAdvancedBillingClient(options =>
        {
            options.BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey, Password = "x" };
            options.Environment = settings.IsEuEnvironment ? ServerEnvironment.Eu : ServerEnvironment.Us;

            var hasExplicitBaseUrl = !string.IsNullOrWhiteSpace(settings.BaseUrl);
            if (settings.IsEuEnvironment)
            {
                if (hasExplicitBaseUrl)
                {
                    options.Server.Production.Eu.BaseUrl = settings.BaseUrl!;
                }
                else
                {
                    options.Server.Production.Eu.Site = settings.Subdomain;
                }
            }
            else
            {
                if (hasExplicitBaseUrl)
                {
                    options.Server.Production.Us.BaseUrl = settings.BaseUrl!;
                }
                else
                {
                    options.Server.Production.Us.Site = settings.Subdomain;
                }
            }
        });

        services.AddScoped<IBillingClient, MaxioBillingClient>();

        return services;
    }
}
