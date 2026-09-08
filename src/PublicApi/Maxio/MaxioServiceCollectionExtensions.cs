using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public static class MaxioServiceCollectionExtensions
{
    public const string HttpClientName = "Maxio.AdvancedBilling";

    /// <summary>
    /// Registers the Maxio Advanced Billing client and the application subscription service.
    /// The client is only constructed on first use, so a host that never serves the subscription
    /// endpoints does not require Maxio configuration.
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.CONFIG_SECTION_NAME));

        services.AddSingleton<AsyncKeyedLock>();

        services.AddHttpClient(HttpClientName, client =>
            {
                // Bounds one attempt; the retry pipeline sits above SendAsync, so this is also the
                // backstop for a hung provider. The whole call is additionally bounded in the service.
                client.Timeout = TimeSpan.FromSeconds(15);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // Keep DNS fresh behind the long-lived client the factory hands out.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new MaxioAdvancedBillingClient(httpClient, BuildClientOptions(settings));
        });

        services.AddScoped<ISubscriptionService, MaxioSubscriptionService>();

        return services;
    }

    /// <summary>
    /// Maps the MAXIO_* environment variables into the "Maxio" configuration section so the same
    /// build runs against any Maxio site/catalog without code changes. Values are never hard-coded.
    /// </summary>
    public static IConfigurationBuilder AddMaxioEnvironmentVariables(this IConfigurationBuilder builder)
    {
        var values = new System.Collections.Generic.Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [$"{MaxioSettings.CONFIG_SECTION_NAME}:ApiKey"] = Environment.GetEnvironmentVariable("MAXIO_API_KEY"),
            [$"{MaxioSettings.CONFIG_SECTION_NAME}:Subdomain"] = Environment.GetEnvironmentVariable("MAXIO_SITE_SUBDOMAIN"),
            [$"{MaxioSettings.CONFIG_SECTION_NAME}:ProductFamilyHandle"] = Environment.GetEnvironmentVariable("MAXIO_DEFAULT_PRODUCT_FAMILY"),
            [$"{MaxioSettings.CONFIG_SECTION_NAME}:BaseUrl"] = Environment.GetEnvironmentVariable("MAXIO_BASE_URL")
        };

        return builder.AddInMemoryCollection(values);
    }

    private static MaxioAdvancedBillingClientOptions BuildClientOptions(MaxioSettings settings)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials
            {
                Username = settings.ApiKey,
                Password = "x"
            },
            Environment = ServerEnvironment.Us,
            Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(15) }
        };

        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            // Explicit override: use verbatim instead of deriving from the subdomain.
            options.Server.Production.Us.BaseUrl = settings.BaseUrl;
        }
        else
        {
            options.Server.Production.Us.Site = settings.Subdomain;
        }

        return options;
    }
}
