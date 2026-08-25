using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Maxio Advanced Billing integration. Settings are bound from
    /// the "Maxio" configuration section (Maxio:ApiKey, Maxio:Subdomain,
    /// Maxio:ProductFamilyHandle, Maxio:BaseUrl).
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(MaxioSettings.CONFIG_NAME).Get<MaxioSettings>() ?? new MaxioSettings();

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new InvalidOperationException(
                "Maxio:ApiKey is not configured. Provide it via user-secrets or the MAXIO_API_KEY environment variable.");
        }

        if (string.IsNullOrWhiteSpace(settings.BaseUrl) && string.IsNullOrWhiteSpace(settings.Subdomain))
        {
            throw new InvalidOperationException(
                "Maxio:Subdomain is not configured. Provide it via user-secrets or the MAXIO_SITE_SUBDOMAIN environment variable.");
        }

        if (string.IsNullOrWhiteSpace(settings.ProductFamilyHandle))
        {
            throw new InvalidOperationException(
                "Maxio:ProductFamilyHandle is not configured. Provide it via user-secrets or the MAXIO_DEFAULT_PRODUCT_FAMILY environment variable.");
        }

        services.AddSingleton(settings);
        services.AddScoped<ISubscriptionBillingService, SubscriptionBillingService>();

        services.AddHttpClient<IMaxioBillingClient, MaxioBillingClient>(client =>
        {
            client.BaseAddress = settings.GetBaseAddress();
            // Per the spec's BasicAuth security scheme: username is the API key, password is "x".
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:x")));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        });

        return services;
    }
}
