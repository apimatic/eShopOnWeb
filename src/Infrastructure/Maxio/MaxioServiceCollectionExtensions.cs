using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Maxio Advanced Billing integration: options bound from the "Maxio"
    /// configuration section (validated at startup), the spec-driven HTTP client, and the
    /// billing service orchestrator.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioOptions>()
            .Bind(configuration.GetSection(MaxioOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<MaxioOptions>, MaxioOptionsValidator>();
        services.AddHttpClient<MaxioClient>();
        services.AddScoped<ISubscriptionBillingService, SubscriptionBillingService>();

        return services;
    }
}

/// <summary>
/// Fail-fast validation of the Maxio settings: the API key and product family handle are always
/// required; the site subdomain is required unless a full BaseUrl override is provided.
/// </summary>
public class MaxioOptionsValidator : IValidateOptions<MaxioOptions>
{
    public ValidateOptionsResult Validate(string? name, MaxioOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            failures.Add($"'{MaxioOptions.SectionName}:ApiKey' is required (source: MAXIO_API_KEY).");
        }
        if (string.IsNullOrWhiteSpace(options.ProductFamilyHandle))
        {
            failures.Add($"'{MaxioOptions.SectionName}:ProductFamilyHandle' is required (source: MAXIO_DEFAULT_PRODUCT_FAMILY).");
        }
        if (string.IsNullOrWhiteSpace(options.BaseUrl) && string.IsNullOrWhiteSpace(options.Subdomain))
        {
            failures.Add($"'{MaxioOptions.SectionName}:Subdomain' is required when '{MaxioOptions.SectionName}:BaseUrl' is not set (source: MAXIO_SITE_SUBDOMAIN).");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
