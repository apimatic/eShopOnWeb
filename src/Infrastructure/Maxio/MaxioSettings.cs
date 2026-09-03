namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Strongly-typed binding of the <c>Maxio:</c> configuration section. Values are supplied by
/// configuration (user-secrets / environment) and never hard-coded, so the same build runs against
/// a different Maxio site and catalog.
/// </summary>
public class MaxioSettings
{
    public const string CONFIG_NAME = "Maxio";

    /// <summary>Maxio API key (used as the HTTP Basic username). From <c>MAXIO_API_KEY</c>.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Maxio site subdomain; the API base is derived as https://{Subdomain}.chargify.com. From <c>MAXIO_SITE_SUBDOMAIN</c>.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family whose products are the subscribable plans. From <c>MAXIO_DEFAULT_PRODUCT_FAMILY</c>.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>Optional explicit API base URL. When set, it is used verbatim instead of deriving one from <see cref="Subdomain"/>.</summary>
    public string? BaseUrl { get; set; }
}
