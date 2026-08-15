namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Strongly-typed Maxio Advanced Billing configuration, bound from the <c>Maxio</c> configuration
/// section. Values are supplied via configuration/user-secrets/environment variables and are never
/// hard-coded, so the same build runs against a different Maxio site and catalog.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio API key (from <c>MAXIO_API_KEY</c>). Used as the Basic-auth username.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Maxio site subdomain (from <c>MAXIO_SITE_SUBDOMAIN</c>), e.g. the sandbox site.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family that holds the subscription plans (from <c>MAXIO_DEFAULT_PRODUCT_FAMILY</c>).</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional explicit API base-URL override. When set, it is used verbatim as the API base
    /// address instead of deriving one from <see cref="Subdomain"/> and <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Maxio data-center/environment selector: <c>US</c> (default) or <c>EU</c> (from
    /// <c>MAXIO_ENVIRONMENT</c>). Selects which server URL template the SDK uses.
    /// </summary>
    public string Environment { get; set; } = "US";
}
