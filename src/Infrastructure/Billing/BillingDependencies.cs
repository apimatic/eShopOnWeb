using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public static class BillingDependencies
{
    private const string HTTP_CLIENT_NAME = "maxio";

    /// <summary>
    /// Registers the Maxio-backed subscription billing service. Settings are read from the "Maxio" section
    /// (falling back to the MAXIO_* environment variables) and validated lazily on first use, so a host without
    /// billing configured still starts and the billing endpoints answer 503.
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.CONFIG_NAME));
        services.PostConfigure<MaxioSettings>(s =>
        {
            s.ApiKey ??= configuration["MAXIO_API_KEY"];
            s.Subdomain ??= configuration["MAXIO_SITE_SUBDOMAIN"];
            s.ProductFamilyHandle ??= configuration["MAXIO_DEFAULT_PRODUCT_FAMILY"];
            s.Environment ??= configuration["MAXIO_ENVIRONMENT"];
        });

        // Named client with a rotating connection pool; the SDK client itself is long-lived.
        services.AddHttpClient(HTTP_CLIENT_NAME)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            });

        services.AddSingleton<ISubscriptionBillingService>(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            var lazyClient = new Lazy<MaxioAdvancedBillingClient>(() =>
                MaxioClientFactory.Create(sp.GetRequiredService<IHttpClientFactory>().CreateClient(HTTP_CLIENT_NAME), settings));

            return new MaxioSubscriptionBillingService(
                () => lazyClient.Value,
                settings.ProductFamilyHandle ?? string.Empty,
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<MaxioSubscriptionBillingService>>());
        });

        return services;
    }
}
