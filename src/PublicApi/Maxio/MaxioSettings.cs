namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the
/// <c>Maxio:</c> configuration section. Values are supplied via environment
/// variables / user-secrets and are never hard-coded.
/// </summary>
public class MaxioSettings
{
    public const string CONFIG_SECTION_NAME = "Maxio";

    /// <summary>Maxio Advanced Billing API key (from <c>MAXIO_API_KEY</c>).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Maxio site subdomain (from <c>MAXIO_SITE_SUBDOMAIN</c>).</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family that contains the subscription plans (from <c>MAXIO_DEFAULT_PRODUCT_FAMILY</c>).</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the API base address. When set, it is used verbatim
    /// instead of deriving <c>https://{subdomain}.chargify.com</c> from the subdomain.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// The base address of the Maxio Advanced Billing API, per the OpenAPI spec
    /// server template <c>https://{site}.chargify.com</c>.
    /// </summary>
    public string ResolvedBaseUrl =>
        !string.IsNullOrWhiteSpace(BaseUrl)
            ? BaseUrl.TrimEnd('/')
            : $"https://{Subdomain}.chargify.com";
}
