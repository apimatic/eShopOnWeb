using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Maxio Advanced Billing integration. Settings bind from the "Maxio"
    /// configuration section (ApiKey, Subdomain, ProductFamilyHandle, BaseUrl) and are
    /// validated at startup so a misconfigured host fails fast.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.SectionName))
            .Validate(s => !string.IsNullOrWhiteSpace(s.ApiKey),
                "Maxio:ApiKey is required (supply via the MAXIO_API_KEY environment variable / user-secrets).")
            .Validate(s => !string.IsNullOrWhiteSpace(s.ProductFamilyHandle),
                "Maxio:ProductFamilyHandle is required (supply via the MAXIO_DEFAULT_PRODUCT_FAMILY environment variable / user-secrets).")
            .Validate(s => !string.IsNullOrWhiteSpace(s.BaseUrl) || !string.IsNullOrWhiteSpace(s.Subdomain),
                "Either Maxio:BaseUrl or Maxio:Subdomain (MAXIO_SITE_SUBDOMAIN) is required.");
        // Note: validation runs lazily on first options resolution (i.e. the first billing
        // call) rather than at host startup, so hosts/tests that never touch the billing
        // endpoints don't need the Maxio secrets configured.

        services.AddHttpClient<IMaxioClient, MaxioClient>((sp, httpClient) =>
        {
            var settings = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<MaxioSettings>>().Value;
            httpClient.BaseAddress = new Uri(settings.ResolveBaseAddress() + "/");
            // Per the spec's BasicAuth security scheme: username is the API key, password is "x".
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:x")));
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        });

        services.AddScoped<ISubscriptionBillingService, MaxioBillingService>();

        return services;
    }
}
