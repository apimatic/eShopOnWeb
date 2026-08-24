namespace Microsoft.eShopWeb;

/// <summary>
/// Settings bound from the "Maxio" configuration section. Values are supplied via
/// user-secrets / environment variables (MAXIO_API_KEY, MAXIO_SITE_SUBDOMAIN,
/// MAXIO_DEFAULT_PRODUCT_FAMILY) — never from source-controlled files.
/// </summary>
public class MaxioSettings
{
    public const string ConfigName = "Maxio";

    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional override. When set, it is used verbatim as the Maxio API base address
    /// instead of deriving one from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// "US" (default) or "EU". Falls back to the MAXIO_ENVIRONMENT variable when unset.
    /// </summary>
    public string? Environment { get; set; }
}
