using System;
using System.Collections.Generic;
using System.Net.Http;
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

public static class MaxioServiceCollectionExtensions
{
    public const string HttpClientName = "MaxioAdvancedBilling";

    // Conventional deployment variable names, accepted as a fallback for the Maxio:* keys.
    private static readonly IReadOnlyDictionary<string, string> EnvironmentAliases = new Dictionary<string, string>
    {
        ["Maxio:ApiKey"] = "MAXIO_API_KEY",
        ["Maxio:Subdomain"] = "MAXIO_SITE_SUBDOMAIN",
        ["Maxio:ProductFamilyHandle"] = "MAXIO_DEFAULT_PRODUCT_FAMILY",
        ["Maxio:Environment"] = "MAXIO_ENVIRONMENT"
    };

    /// <summary>
    /// Fills any <c>Maxio:*</c> key that no configuration source supplied from its conventional
    /// <c>MAXIO_*</c> environment variable. Configured values always win.
    /// </summary>
    public static void AddMaxioEnvironmentFallbacks(this IConfigurationManager configuration)
    {
        foreach (var (key, variable) in EnvironmentAliases)
        {
            if (!string.IsNullOrWhiteSpace(configuration[key]))
            {
                continue;
            }
            var value = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(value))
            {
                configuration[key] = value;
            }
        }
    }

    /// <summary>
    /// Registers subscription billing backed by Maxio Advanced Billing. The host refuses to start when
    /// a required <c>Maxio:*</c> setting is missing.
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<MaxioSettings>, MaxioSettingsValidator>();

        services.AddHttpClient(HttpClientName, (sp, client) =>
            {
                // Backstop per attempt; the SDK's own per-attempt timeout fires first.
                var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
                client.Timeout = settings.AttemptTimeout + TimeSpan.FromSeconds(5);
            })
            // The SDK client is a singleton holding one HttpClient; recycle pooled connections so DNS changes are seen.
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        // Options are read once here: a rotated key takes effect on the next process start.
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return MaxioClientFactory.Create(httpClient, settings, sp.GetRequiredService<ILoggerFactory>());
        });

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<MaxioBillingGateway>();
        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }
}
