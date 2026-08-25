namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Settings bound from the "Maxio" configuration section. Values are supplied via
/// .NET user-secrets or environment variables (MAXIO_API_KEY, MAXIO_SITE_SUBDOMAIN,
/// MAXIO_DEFAULT_PRODUCT_FAMILY) — never hard-coded.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override. When set, used verbatim as the API base address instead of
    /// deriving one from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }
}
