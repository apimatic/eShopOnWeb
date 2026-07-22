using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Configuration;

/// <summary>
/// Registers the Maxio billing seam. Both hosts call this from their own composition root, so the
/// provider is wired in exactly one place and touched by exactly one class.
/// </summary>
public static class MaxioBillingServiceCollectionExtensions
{
    /// <summary>The name of the long-lived, factory-managed HttpClient the provider SDK is given.</summary>
    public const string HttpClientName = "maxio";

    private const int DefaultTimeoutSeconds = 30;
    private const int DefaultCatalogCacheMinutes = 30;

    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.ConfigurationSection));

        services.AddSingleton(sp => sp.GetRequiredService<IOptions<MaxioSettings>>().Value);

        // The SDK does not own its HttpClient: one long-lived, factory-managed instance is supplied so
        // handlers are pooled and recycled, and so tests can substitute the transport.
        services.AddHttpClient(HttpClientName, (sp, http) =>
        {
            var settings = sp.GetRequiredService<MaxioSettings>();
            var seconds = settings.TimeoutSeconds > 0 ? settings.TimeoutSeconds : DefaultTimeoutSeconds;
            http.Timeout = TimeSpan.FromSeconds(seconds);
        });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<MaxioSettings>();
            var minutes = settings.CatalogCacheMinutes > 0 ? settings.CatalogCacheMinutes : DefaultCatalogCacheMinutes;
            return new MaxioCatalogCache(TimeSpan.FromMinutes(minutes));
        });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<MaxioSettings>();
            settings.Validate();

            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new MaxioAdvancedBillingClient(httpClient, MaxioClientOptionsFactory.Create(settings));
        });

        services.AddScoped<IBillingClient, MaxioBillingClient>();

        return services;
    }
}
