namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the "Maxio" configuration section.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>The Maxio API key (from MAXIO_API_KEY).</summary>
    public string? ApiKey { get; set; }

    /// <summary>The Maxio site subdomain (from MAXIO_SITE_SUBDOMAIN).</summary>
    public string? Subdomain { get; set; }

    /// <summary>The handle of the product family that holds the subscription plans (from MAXIO_DEFAULT_PRODUCT_FAMILY).</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional override for the Maxio API base address. When set, it is used verbatim instead of
    /// deriving one from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// The API base address to call. Uses <see cref="BaseUrl"/> verbatim when provided, otherwise
    /// derives the standard US Advanced Billing endpoint from <see cref="Subdomain"/>.
    /// </summary>
    public string ResolvedBaseUrl =>
        !string.IsNullOrWhiteSpace(BaseUrl)
            ? BaseUrl
            : $"https://{Subdomain}.chargify.com";
}
