namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Strongly-typed Maxio configuration, bound from the "Maxio" section. Values are supplied at
/// runtime (user-secrets / environment) and are never hard-coded, so the same build runs against
/// a different Maxio site and catalog.
/// </summary>
public class MaxioSettings
{
    public const string CONFIG_NAME = "Maxio";

    /// <summary>Maxio API key — the Basic-auth username. From MAXIO_API_KEY.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Site subdomain used to derive the API base address. From MAXIO_SITE_SUBDOMAIN.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family whose plans are offered. From MAXIO_DEFAULT_PRODUCT_FAMILY.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional verbatim base-URL override. When set, it is used as-is instead of deriving the
    /// address from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }
}
