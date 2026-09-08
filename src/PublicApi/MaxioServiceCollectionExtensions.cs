using System;
using System.Collections.Generic;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Composition-root registration for the Maxio Advanced Billing integration.
/// </summary>
public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Maps the well-known Maxio environment variables onto the "Maxio" configuration
    /// section so settings can be bound with exactly these keys:
    ///   MAXIO_API_KEY                -> Maxio:ApiKey
    ///   MAXIO_SITE_SUBDOMAIN         -> Maxio:Subdomain
    ///   MAXIO_DEFAULT_PRODUCT_FAMILY -> Maxio:ProductFamilyHandle
    ///   MAXIO_BASE_URL               -> Maxio:BaseUrl (optional override)
    /// Only variable/secret names are referenced here - never their values.
    /// </summary>
    public static IConfigurationBuilder AddMaxioEnvironmentConfiguration(this IConfigurationBuilder configuration)
    {
        var mappings = new Dictionary<string, string>
        {
            ["MAXIO_API_KEY"] = "Maxio:ApiKey",
            ["MAXIO_SITE_SUBDOMAIN"] = "Maxio:Subdomain",
            ["MAXIO_DEFAULT_PRODUCT_FAMILY"] = "Maxio:ProductFamilyHandle",
            ["MAXIO_BASE_URL"] = "Maxio:BaseUrl",
        };

        var values = new Dictionary<string, string?>();
        foreach (var mapping in mappings)
        {
            var value = Environment.GetEnvironmentVariable(mapping.Key);
            if (!string.IsNullOrWhiteSpace(value))
            {
                values[mapping.Value] = value;
            }
        }

        if (values.Count > 0)
        {
            configuration.AddInMemoryCollection(values);
        }

        return configuration;
    }

    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(MaxioOptions.CONFIG_SECTION_NAME);
        var options = new MaxioOptions
        {
            ApiKey = section["ApiKey"] ?? string.Empty,
            Subdomain = section["Subdomain"] ?? string.Empty,
            ProductFamilyHandle = section["ProductFamilyHandle"] ?? string.Empty,
            BaseUrl = section["BaseUrl"],
        };

        services.AddSingleton(options);
        services.AddSingleton<IMaxioApiClient>(_ => new MaxioApiClient(
            new HttpClient { Timeout = TimeSpan.FromSeconds(60) },
            options));
        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }
}
