using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Maps MAXIO_* environment variables onto the Maxio:* configuration section.
    /// When MAXIO_ENVIRONMENT is EU and Maxio:BaseUrl is empty, the EU host is used.
    /// </summary>
    public static void AddMaxioEnvironmentOverrides(this IConfigurationManager configuration)
    {
        var overrides = new Dictionary<string, string?>();

        var apiKey = Environment.GetEnvironmentVariable("MAXIO_API_KEY");
        var subdomain = Environment.GetEnvironmentVariable("MAXIO_SITE_SUBDOMAIN");
        var family = Environment.GetEnvironmentVariable("MAXIO_DEFAULT_PRODUCT_FAMILY");
        var environment = Environment.GetEnvironmentVariable("MAXIO_ENVIRONMENT");

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            overrides["Maxio:ApiKey"] = apiKey;
        }

        if (!string.IsNullOrWhiteSpace(subdomain))
        {
            overrides["Maxio:Subdomain"] = subdomain;
        }

        if (!string.IsNullOrWhiteSpace(family))
        {
            overrides["Maxio:ProductFamilyHandle"] = family;
        }

        var existingBaseUrl = configuration["Maxio:BaseUrl"];
        if (string.IsNullOrWhiteSpace(existingBaseUrl)
            && !string.IsNullOrWhiteSpace(subdomain)
            && string.Equals(environment, "EU", StringComparison.OrdinalIgnoreCase))
        {
            overrides["Maxio:BaseUrl"] = $"https://{subdomain.Trim()}.ebilling.maxio.com";
        }

        if (overrides.Count > 0)
        {
            configuration.AddInMemoryCollection(overrides);
        }
    }

    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioOptions>(configuration.GetSection(MaxioOptions.SectionName));
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<MaxioOptions>>().Value);

        services.AddHttpClient<IMaxioBillingClient, MaxioBillingClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<MaxioOptions>();
            if (!options.IsConfigured)
            {
                return;
            }

            client.BaseAddress = new Uri(options.ResolveBaseUrl(), UriKind.Absolute);
            client.Timeout = TimeSpan.FromSeconds(100);
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.Authorization = MaxioBillingClient.CreateBasicAuthHeader(options.ApiKey);
        });

        services.AddScoped<ISubscriptionService, SubscriptionService>();
        return services;
    }
}
