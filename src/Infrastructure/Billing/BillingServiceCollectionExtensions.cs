using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public static class BillingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Maxio-backed subscription billing service. Settings are bound from the
    /// "Maxio" configuration section; as a convenience for local development the MAXIO_API_KEY,
    /// MAXIO_SITE_SUBDOMAIN and MAXIO_DEFAULT_PRODUCT_FAMILY environment variables are used
    /// as fallbacks when the corresponding Maxio:* keys are not set.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioSettings>().Configure<IConfiguration>((settings, config) =>
        {
            config.GetSection(MaxioSettings.SectionName).Bind(settings);
            settings.ApiKey = EmptyToNull(settings.ApiKey) ?? EmptyToNull(config["MAXIO_API_KEY"]);
            settings.Subdomain = EmptyToNull(settings.Subdomain) ?? EmptyToNull(config["MAXIO_SITE_SUBDOMAIN"]);
            settings.ProductFamilyHandle = EmptyToNull(settings.ProductFamilyHandle) ?? EmptyToNull(config["MAXIO_DEFAULT_PRODUCT_FAMILY"]);
            settings.BaseUrl = EmptyToNull(settings.BaseUrl);
        });

        services.AddHttpClient<ISubscriptionBillingService, MaxioSubscriptionBillingService>((sp, client) =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            settings.Validate();

            client.BaseAddress = settings.GetBaseAddress();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:x")));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        return services;
    }

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
