using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the "Maxio" configuration section.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio Advanced Billing API key (from MAXIO_API_KEY).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Subdomain of the Maxio Advanced Billing site (from MAXIO_SITE_SUBDOMAIN).</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family that holds the subscription plans (from MAXIO_DEFAULT_PRODUCT_FAMILY).</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>Optional override for the API base address. When set, used verbatim instead of deriving one from the subdomain.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Advanced Billing hosting environment (US or EU). Used only when <see cref="BaseUrl"/> is not set.</summary>
    public string Environment { get; set; } = "US";

    /// <summary>
    /// Resolves the API base address. Uses <see cref="BaseUrl"/> verbatim when set, otherwise derives it from
    /// <see cref="Subdomain"/> and <see cref="Environment"/>.
    /// </summary>
    public Uri GetBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return new Uri(BaseUrl, UriKind.Absolute);
        }

        string host = string.Equals(Environment, "EU", StringComparison.OrdinalIgnoreCase)
            ? $"{Subdomain}.ebilling.maxio.com"
            : $"{Subdomain}.chargify.com";

        return new Uri($"https://{host}", UriKind.Absolute);
    }
}
