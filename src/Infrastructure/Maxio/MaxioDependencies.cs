using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioDependencies
{
    /// <summary>
    /// Registers the Maxio subscription billing stack: options bound from the
    /// "Maxio" configuration section, the spec-contract API client, and the
    /// subscription billing service. Secret values come from user-secrets or
    /// environment variables; they are never hard-coded.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioOptions>(configuration.GetSection(MaxioOptions.SectionName));
        services.AddHttpClient<IMaxioApiClient, MaxioApiClient>();
        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();
        return services;
    }
}