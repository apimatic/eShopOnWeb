using System;
using System.Collections.Generic;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    // Per-attempt bounds; the whole-call budget is enforced in MaxioSubscriptionService.
    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Registers the Maxio-backed subscription service. Throws at startup when a required
    /// "Maxio:" setting is missing or blank, instead of failing on the first request.
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptions(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(MaxioSettings.CONFIG_NAME).Get<MaxioSettings>() ?? new MaxioSettings();
        Validate(settings);

        services.AddSingleton(settings);
        services.AddSingleton(sp =>
        {
            // Built once: a rotated API key takes effect after a restart.
            var httpClient = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
            {
                Timeout = TimeSpan.FromSeconds(15)
            };

            var options = new MaxioAdvancedBillingClientOptions
            {
                BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey!, Password = "x" },
                Retry = RetryOptions.Default() with
                {
                    Timeout = AttemptTimeout,
                    MaxRetries = 2,
                    // The application owns write retries (claim + reconciliation); the SDK only resends reads.
                    HttpMethodsToRetry = new List<HttpMethod> { HttpMethod.Get }
                },
                // Explicit factory so the SDK's env-var log switch can never arm request-body logging.
                Logging = new LoggingOptions { LoggerFactory = sp.GetRequiredService<ILoggerFactory>() }
            };

            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Production.Us.BaseUrl = settings.BaseUrl!;
            }
            else
            {
                options.Server.Production.Us.Site = settings.Subdomain!;
            }

            return new MaxioAdvancedBillingClient(httpClient, options);
        });
        services.AddScoped<ISubscriptionService, MaxioSubscriptionService>();
        return services;
    }

    private static void Validate(MaxioSettings settings)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(settings.ApiKey)) missing.Add($"{MaxioSettings.CONFIG_NAME}:{nameof(MaxioSettings.ApiKey)}");
        if (string.IsNullOrWhiteSpace(settings.ProductFamilyHandle)) missing.Add($"{MaxioSettings.CONFIG_NAME}:{nameof(MaxioSettings.ProductFamilyHandle)}");
        if (string.IsNullOrWhiteSpace(settings.BaseUrl) && string.IsNullOrWhiteSpace(settings.Subdomain))
            missing.Add($"{MaxioSettings.CONFIG_NAME}:{nameof(MaxioSettings.Subdomain)}");
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Maxio billing is not configured. Set: {string.Join(", ", missing)} (user-secrets or environment).");
        }

        if (!string.IsNullOrWhiteSpace(settings.BaseUrl) && !Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException($"{MaxioSettings.CONFIG_NAME}:{nameof(MaxioSettings.BaseUrl)} must be an absolute URL.");
        }
    }
}
