using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Registers the Maxio Advanced Billing client and <see cref="ISubscriptionService"/>.
///
/// Config contract (values are never hard-coded; they come from environment variables / user
/// secrets bound into the "Maxio" section):
///   Maxio:ApiKey, Maxio:Subdomain, Maxio:ProductFamilyHandle, Maxio:BaseUrl (optional override).
/// Server environment (US/EU) is selected from MAXIO_ENVIRONMENT when present (default: US); the
/// SDK's only hosting nodes are US (chargify.com) and EU (ebilling.maxio.com), so a sandbox or any
/// other base is reached through the Maxio:BaseUrl override, which is used verbatim when set.
/// </summary>
public static class MaxioServiceCollectionExtensions
{
    public const string MaxioHttpClientName = "MaxioAdvancedBilling";

    private static readonly TimeSpan _overallCallBudget = TimeSpan.FromSeconds(60);

    public static IServiceCollection AddMaxioSubscriptionBilling(
        this IServiceCollection services, IConfiguration configuration)
    {
        var settings = new MaxioSettings
        {
            ApiKey = configuration[$"{MaxioSettings.SectionName}:ApiKey"] ?? string.Empty,
            Subdomain = configuration[$"{MaxioSettings.SectionName}:Subdomain"] ?? string.Empty,
            ProductFamilyHandle = configuration[$"{MaxioSettings.SectionName}:ProductFamilyHandle"] ?? string.Empty,
            BaseUrl = configuration[$"{MaxioSettings.SectionName}:BaseUrl"] ?? string.Empty,
        };

        bool isEu = string.Equals(
            configuration["MAXIO_ENVIRONMENT"] ?? "US", "EU", StringComparison.OrdinalIgnoreCase);

        services.AddSingleton(settings);
        services.AddTransient<MaxioWriteOnceHandler>();

        // Named HttpClient so the timeout / handler pipeline is scoped to Maxio (the default client
        // is shared with every other unnamed consumer). The SDK client is long-lived, so the primary
        // handler gets a pooled-connection lifetime to keep DNS fresh.
        services.AddHttpClient(MaxioHttpClientName, client => client.Timeout = TimeSpan.FromSeconds(25))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            })
            .AddHttpMessageHandler<MaxioWriteOnceHandler>();

        services.AddSingleton(sp => CreateClient(sp, settings, isEu));
        services.AddScoped<ISubscriptionService, MaxioSubscriptionService>();

        return services;
    }

    /// <summary>Overall budget the service applies around each Maxio operation.</summary>
    public static TimeSpan OverallCallBudget => _overallCallBudget;

    private static MaxioAdvancedBillingClient CreateClient(
        IServiceProvider serviceProvider, MaxioSettings settings, bool isEu)
    {
        // Validated lazily (not at registration) so a host that never uses the Maxio capability - e.g.
        // the integration-test host - can start without Maxio credentials configured. The first real
        // use fails fast with a clear configuration error.
        ValidateSettings(settings);

        var httpClient = serviceProvider
            .GetRequiredService<IHttpClientFactory>()
            .CreateClient(MaxioHttpClientName);

        var options = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials
            {
                Username = settings.ApiKey,
                Password = "x",
            },
            Retry = RetryOptions.Default() with
            {
                // The write-once handler already prevents a duplicate POST on transport failure, so
                // reads keep a modest transport retry. The SDK floor is 1 (0 is rejected).
                MaxRetries = 2,
                Timeout = TimeSpan.FromSeconds(20),
                UseExponentialBackoff = true,
            },
        };

        bool hasBaseUrlOverride = !string.IsNullOrWhiteSpace(settings.BaseUrl);

        if (isEu)
        {
            options.Environment = ServerEnvironment.Eu;
            if (hasBaseUrlOverride)
            {
                options.Server.Production.Eu.BaseUrl = settings.BaseUrl.Trim();
            }
            else
            {
                options.Server.Production.Eu.Site = settings.Subdomain;
            }
        }
        else
        {
            options.Environment = ServerEnvironment.Us;
            if (hasBaseUrlOverride)
            {
                options.Server.Production.Us.BaseUrl = settings.BaseUrl.Trim();
            }
            else
            {
                options.Server.Production.Us.Site = settings.Subdomain;
            }
        }

        return new MaxioAdvancedBillingClient(httpClient, options);
    }

    private static void ValidateSettings(MaxioSettings settings)
    {
        var missing = new System.Collections.Generic.List<string>();
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            missing.Add("Maxio:ApiKey");
        }

        if (string.IsNullOrWhiteSpace(settings.BaseUrl) && string.IsNullOrWhiteSpace(settings.Subdomain))
        {
            missing.Add("Maxio:Subdomain (or Maxio:BaseUrl)");
        }

        if (string.IsNullOrWhiteSpace(settings.ProductFamilyHandle))
        {
            missing.Add("Maxio:ProductFamilyHandle");
        }

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                "Maxio billing is not configured. Set configuration values for: " +
                string.Join(", ", missing) +
                " (these map from MAXIO_API_KEY / MAXIO_SITE_SUBDOMAIN / MAXIO_DEFAULT_PRODUCT_FAMILY).");
        }
    }
}
