using System;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioDependencies
{
    /// <summary>
    /// Registers the Maxio Advanced Billing integration: options bound from the
    /// <c>Maxio</c> configuration section, the Billing API HTTP client, and the
    /// subscription orchestration service.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioOptions>()
            .Bind(configuration.GetSection(MaxioOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.ApiKey), "Maxio:ApiKey is required")
            .Validate(o => !string.IsNullOrWhiteSpace(o.ProductFamilyHandle), "Maxio:ProductFamilyHandle is required")
            .Validate(o => !string.IsNullOrWhiteSpace(o.BaseUrl) || !string.IsNullOrWhiteSpace(o.Subdomain),
                "Maxio:Subdomain is required unless Maxio:BaseUrl is set")
            .ValidateOnStart();

        services.AddHttpClient<IMaxioClient, MaxioClient>();
        services.AddScoped<ISubscriptionService, SubscriptionService>();

        return services;
    }
}