using System;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Bound from the "Maxio" configuration section. Values come from user-secrets or the
/// MAXIO_* environment variables — never from a file in the repository.
/// </summary>
public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }
    public string? ProductFamilyHandle { get; set; }

    /// <summary>Optional verbatim override of the API base address.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Maxio hosting environment: US or EU. Defaults to US.</summary>
    public string? Environment { get; set; }

    public MaxioAdvancedBilling.Servers.ServerEnvironment ServerEnvironment =>
        string.Equals(Environment, "EU", StringComparison.OrdinalIgnoreCase)
            ? MaxioAdvancedBilling.Servers.ServerEnvironment.Eu
            : MaxioAdvancedBilling.Servers.ServerEnvironment.Us;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(Subdomain) &&
        !string.IsNullOrWhiteSpace(ProductFamilyHandle);

    public static MaxioOptions Load(IConfiguration configuration)
    {
        var options = new MaxioOptions();
        configuration.GetSection(SectionName).Bind(options);

        options.ApiKey = FirstNonEmpty(options.ApiKey, System.Environment.GetEnvironmentVariable("MAXIO_API_KEY"));
        options.Subdomain = FirstNonEmpty(options.Subdomain, System.Environment.GetEnvironmentVariable("MAXIO_SITE_SUBDOMAIN"));
        options.ProductFamilyHandle = FirstNonEmpty(options.ProductFamilyHandle, System.Environment.GetEnvironmentVariable("MAXIO_DEFAULT_PRODUCT_FAMILY"));
        options.Environment = FirstNonEmpty(options.Environment, System.Environment.GetEnvironmentVariable("MAXIO_ENVIRONMENT"));

        return options;
    }

    private static string? FirstNonEmpty(string? first, string? second) =>
        !string.IsNullOrWhiteSpace(first) ? first : second;
}