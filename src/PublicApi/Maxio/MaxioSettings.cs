namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Settings bound from the "Maxio" configuration section. Values are supplied via
/// environment variables (MAXIO_API_KEY, MAXIO_SITE_SUBDOMAIN, MAXIO_ENVIRONMENT,
/// MAXIO_DEFAULT_PRODUCT_FAMILY) or .NET user-secrets — never hard-coded.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }
    public string? ProductFamilyHandle { get; set; }

    /// <summary>Optional verbatim API base-address override. When set, it wins over the subdomain-derived URL.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>"us" (default) or "eu".</summary>
    public string? Environment { get; set; }
}
