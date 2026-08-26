namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Settings bound from the "Maxio" configuration section. Values are supplied via
/// environment variables / user-secrets (MAXIO_API_KEY, MAXIO_SITE_SUBDOMAIN,
/// MAXIO_DEFAULT_PRODUCT_FAMILY); never hard-code them here.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override. When set, used verbatim as the Maxio API base address
    /// instead of deriving one from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }
}
