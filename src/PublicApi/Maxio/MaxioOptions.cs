namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Settings bound from the <c>Maxio</c> configuration section.
/// Values are supplied through configuration only (user-secrets backed by the
/// <c>MAXIO_API_KEY</c>, <c>MAXIO_SITE_SUBDOMAIN</c>, <c>MAXIO_DEFAULT_PRODUCT_FAMILY</c>
/// and optional <c>MAXIO_BASE_URL</c> environment variables) and are never hard-coded.
/// </summary>
public class MaxioOptions
{
    public const string ConfigurationSectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;

    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family that owns the subscription plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional verbatim API base URL. When set it is used as-is and wins over the
    /// subdomain-derived <c>https://{subdomain}.chargify.com</c> address.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// True when enough settings are present to talk to Maxio: an API key, the product
    /// family handle, and either a base URL override or a site subdomain.
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(ProductFamilyHandle)
        && (!string.IsNullOrWhiteSpace(BaseUrl) || !string.IsNullOrWhiteSpace(Subdomain));
}
