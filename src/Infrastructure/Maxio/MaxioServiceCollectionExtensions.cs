using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Maxio Advanced Billing subscription billing integration.
    /// Settings are bound from the "Maxio" configuration section
    /// (Maxio:ApiKey, Maxio:Subdomain, Maxio:ProductFamilyHandle and the optional
    /// Maxio:BaseUrl override); values come from user-secrets/environment variables.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioOptions>()
            .Bind(configuration.GetSection(MaxioOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.ApiKey),
                "Maxio:ApiKey is required (source: MAXIO_API_KEY).")
            .Validate(options =>
                    !string.IsNullOrWhiteSpace(options.Subdomain) || !string.IsNullOrWhiteSpace(options.BaseUrl),
                "Either Maxio:BaseUrl or Maxio:Subdomain is required (source: MAXIO_SITE_SUBDOMAIN).")
            .Validate(options => !string.IsNullOrWhiteSpace(options.ProductFamilyHandle),
                "Maxio:ProductFamilyHandle is required (source: MAXIO_DEFAULT_PRODUCT_FAMILY).");

        services.AddHttpClient<MaxioApiClient>((serviceProvider, httpClient) =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<MaxioOptions>>().Value;
                httpClient.BaseAddress = new Uri(options.ResolveBaseUrl().TrimEnd('/') + "/");
                httpClient.Timeout = TimeSpan.FromSeconds(30);
            });

        services.AddSingleton<MaxioUserLockRegistry>();
        services.AddScoped<ISubscriptionBillingService, MaxioBillingService>();

        return services;
    }
}
