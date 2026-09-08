using Microsoft.eShopWeb.PublicApi.Subscriptions.Maxio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the Maxio subscription billing capability:
    /// settings binding, the typed Maxio API client and the orchestration service.
    /// </summary>
    public static IServiceCollection AddSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        ApplyEnvironmentFallbacks(configuration);

        services.AddOptions<MaxioBillingOptions>()
            .Bind(configuration.GetSection(MaxioBillingOptions.ConfigurationSectionName))
            .PostConfigure(options =>
            {
                // Resolve the API base address once. An explicit Maxio:BaseUrl wins; otherwise one
                // is derived from the subdomain (+ hosting environment for the *.<host> template).
                if (string.IsNullOrWhiteSpace(options.BaseUrl))
                {
                    options.BaseUrl = options.ResolveBaseUrl(configuration["MAXIO_ENVIRONMENT"]);
                }
            });

        services.AddHttpClient<IMaxioBillingApi, MaxioBillingApi>();

        services.AddScoped<ISubscriptionService, SubscriptionService>();

        return services;
    }

    /// <summary>
    /// Lets a single build run against different Maxio sites/catalogs purely via environment
    /// variables. When the <c>Maxio:*</c> settings are not already configured explicitly
    /// (appsettings, user-secrets or <c>Maxio__*</c> environment variables), they are populated
    /// from the <c>MAXIO_*</c> environment variables. Explicit configuration always wins because
    /// these fallbacks are stored in the lowest-precedence configuration source.
    /// </summary>
    private static void ApplyEnvironmentFallbacks(IConfiguration configuration)
    {
        SetIfMissing(configuration, "Maxio:ApiKey", "MAXIO_API_KEY");
        SetIfMissing(configuration, "Maxio:Subdomain", "MAXIO_SITE_SUBDOMAIN");
        SetIfMissing(configuration, "Maxio:ProductFamilyHandle", "MAXIO_DEFAULT_PRODUCT_FAMILY");
    }

    private static void SetIfMissing(IConfiguration configuration, string targetKey, string environmentKey)
    {
        if (string.IsNullOrWhiteSpace(configuration[targetKey]))
        {
            configuration[targetKey] = configuration[environmentKey];
        }
    }
}
