using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Maxio;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Binds the "Maxio" configuration section and registers the Maxio-backed
    /// <see cref="ISubscriptionBillingService"/> as a typed HttpClient with Basic auth
    /// (API key as username, "x" as password, per the Maxio Advanced Billing docs).
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioOptions>()
            .Bind(configuration.GetSection(MaxioOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.ApiKey),
                "Maxio:ApiKey is required (set the MAXIO_API_KEY environment variable or user-secret).")
            .Validate(options => !string.IsNullOrWhiteSpace(options.ProductFamilyHandle),
                "Maxio:ProductFamilyHandle is required (set the MAXIO_DEFAULT_PRODUCT_FAMILY environment variable or user-secret).")
            .Validate(options => !string.IsNullOrWhiteSpace(options.BaseUrl) || !string.IsNullOrWhiteSpace(options.Subdomain),
                "Either Maxio:BaseUrl or Maxio:Subdomain is required (set the MAXIO_SITE_SUBDOMAIN environment variable or user-secret).")
            .ValidateOnStart();

        services.AddHttpClient<ISubscriptionBillingService, MaxioBillingService>((serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<MaxioOptions>>().Value;
            client.BaseAddress = options.GetBaseAddress();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.ApiKey}:x")));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        return services;
    }
}
