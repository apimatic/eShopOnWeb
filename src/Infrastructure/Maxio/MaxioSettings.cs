namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Strongly-typed Maxio Advanced Billing configuration, bound from the <c>Maxio</c> section.
/// Values are supplied via environment variables / user-secrets and are never committed to the repo.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>API key used as the HTTP Basic username (from <c>MAXIO_API_KEY</c>).</summary>
    public string? ApiKey { get; set; }

    /// <summary>Site subdomain, substituted into the derived base URL (from <c>MAXIO_SITE_SUBDOMAIN</c>).</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family whose products are exposed as plans (from <c>MAXIO_DEFAULT_PRODUCT_FAMILY</c>).</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional explicit base URL. When set, it is used verbatim as the API base address instead of
    /// deriving one from <see cref="Subdomain"/> and <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Server environment selector: <c>US</c> (default) or <c>EU</c> (from <c>MAXIO_ENVIRONMENT</c>).
    /// Determines which regional host the subdomain is derived against when <see cref="BaseUrl"/> is unset.
    /// </summary>
    public string? Environment { get; set; }
}
