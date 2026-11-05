using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public static class BillingServiceCollectionExtensions
{
    /// <summary>
    /// Registers Maxio subscription billing. Settings bind from the "Maxio" section; the client is
    /// created lazily so a host without credentials still starts (billing calls then report 503).
    /// </summary>
    public static IServiceCollection AddSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.CONFIG_NAME))
            .PostConfigure(s =>
            {
                // Fall back to the plain environment variables the sandbox credentials arrive in.
                if (string.IsNullOrWhiteSpace(s.ApiKey)) s.ApiKey = Environment.GetEnvironmentVariable("MAXIO_API_KEY");
                if (string.IsNullOrWhiteSpace(s.Subdomain)) s.Subdomain = Environment.GetEnvironmentVariable("MAXIO_SITE_SUBDOMAIN");
                if (string.IsNullOrWhiteSpace(s.ProductFamilyHandle)) s.ProductFamilyHandle = Environment.GetEnvironmentVariable("MAXIO_DEFAULT_PRODUCT_FAMILY");
                if (string.IsNullOrWhiteSpace(s.Environment)) s.Environment = Environment.GetEnvironmentVariable("MAXIO_ENVIRONMENT");
            });

        services.AddHttpClient(nameof(MaxioAdvancedBillingClient));
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(MaxioAdvancedBillingClient));
            http.Timeout = TimeSpan.FromSeconds(Math.Max(1, settings.TimeoutSeconds));
            return MaxioClientFactory.Create(http, settings);
        });
        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();
        return services;
    }
}
