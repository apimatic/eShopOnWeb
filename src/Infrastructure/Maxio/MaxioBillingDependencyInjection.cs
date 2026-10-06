using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioBillingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Maxio Advanced Billing integration:
    /// - binds and validates the "Maxio" configuration section
    /// - registers the typed API client (HTTP Basic auth, sandbox/production base address)
    /// - registers the subscription billing service used by the PublicApi endpoints
    ///
    /// Credential values are never hard-coded; they are expected from
    /// user-secrets or environment variables (MAXIO_API_KEY,
    /// MAXIO_SITE_SUBDOMAIN, MAXIO_DEFAULT_PRODUCT_FAMILY, optional Maxio:BaseUrl).
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(MaxioOptions.SectionName);
        var options = section.Get<MaxioOptions>() ?? new MaxioOptions();

        var failures = Validate(options);
        if (failures.Count > 0)
        {
            throw new InvalidOperationException(
                "The Maxio billing integration is not configured correctly: " + string.Join(" ", failures));
        }

        services.AddOptions<MaxioOptions>().Bind(section);

        services.AddHttpClient<IMaxioApiClient, MaxioApiClient>((serviceProvider, httpClient) =>
        {
            var boundOptions = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<MaxioOptions>>().Value;
            MaxioHttpClientSetup.Configure(httpClient, boundOptions);
        });

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }

    private static List<string> Validate(MaxioOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            failures.Add("Maxio:ApiKey is missing (set the MAXIO_API_KEY environment variable or the user-secret 'Maxio:ApiKey').");
        }

        if (string.IsNullOrWhiteSpace(options.BaseUrl) && string.IsNullOrWhiteSpace(options.Subdomain))
        {
            failures.Add("Either Maxio:Subdomain (MAXIO_SITE_SUBDOMAIN) or Maxio:BaseUrl must be configured.");
        }

        if (string.IsNullOrWhiteSpace(options.ProductFamilyHandle))
        {
            failures.Add("Maxio:ProductFamilyHandle is missing (set the MAXIO_DEFAULT_PRODUCT_FAMILY environment variable or the user-secret 'Maxio:ProductFamilyHandle').");
        }

        return failures;
    }
}