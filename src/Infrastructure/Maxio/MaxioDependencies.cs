using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioDependencies
{
    /// <summary>
    /// Registers the Maxio Advanced Billing subscription billing services.
    /// Configuration is bound from the "Maxio" section (values provided via
    /// user-secrets / environment: Maxio:ApiKey, Maxio:Subdomain,
    /// Maxio:ProductFamilyHandle and the optional Maxio:BaseUrl override).
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioOptions>(configuration.GetSection(MaxioOptions.SectionName));
        services.AddHttpClient<IMaxioApiClient, MaxioApiClient>();
        services.AddScoped<ISubscriptionService, MaxioSubscriptionService>();
        return services;
    }
}
