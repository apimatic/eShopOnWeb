namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Settings bound from the <c>Maxio:</c> configuration section.
/// Values are provided by the host (environment variables loaded into user-secrets);
/// none of them are hard-coded here so the same build can target another Maxio site/catalog.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio API key (env: MAXIO_API_KEY).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Maxio site subdomain, e.g. "cp-exp-5" (env: MAXIO_SITE_SUBDOMAIN).</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family that holds the subscription plans (env: MAXIO_DEFAULT_PRODUCT_FAMILY).</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional absolute base URL override. When set it is used verbatim instead of deriving
    /// <c>https://{subdomain}.chargify.com</c> from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }
}
