namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Settings bound from the "Maxio" configuration section.
///
/// The same build must run against different Maxio sites/catalogs, so every value comes from
/// configuration (environment variables / user secrets), never from code:
///   Maxio:ApiKey               <- MAXIO_API_KEY
///   Maxio:Subdomain            <- MAXIO_SITE_SUBDOMAIN
///   Maxio:ProductFamilyHandle  <- MAXIO_DEFAULT_PRODUCT_FAMILY
///   Maxio:BaseUrl              (optional override; when set it is used verbatim as the API base
///                               address instead of deriving "https://{subdomain}.chargify.com")
/// </summary>
public class MaxioSettings
{
    public const string CONFIG_SECTION_NAME = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;

    public bool HasCredentials =>
        !string.IsNullOrWhiteSpace(ApiKey)
        && (!string.IsNullOrWhiteSpace(Subdomain) || !string.IsNullOrWhiteSpace(BaseUrl));
}
