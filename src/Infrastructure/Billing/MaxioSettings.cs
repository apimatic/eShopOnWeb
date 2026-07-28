namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Strongly-typed binding of the <c>Maxio:</c> configuration section. Values are supplied via
/// configuration / user-secrets / environment variables and must never be hard-coded — the same
/// build has to run against a different Maxio site and catalog.
/// </summary>
public class MaxioSettings
{
    /// <summary>Configuration section these settings bind from.</summary>
    public const string SectionName = "Maxio";

    /// <summary>Maxio API key (bound from <c>Maxio:ApiKey</c> / env <c>MAXIO_API_KEY</c>). Used as the HTTP Basic username.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Maxio site subdomain (bound from <c>Maxio:Subdomain</c> / env <c>MAXIO_SITE_SUBDOMAIN</c>).</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family whose plans are offered (bound from <c>Maxio:ProductFamilyHandle</c> / env <c>MAXIO_DEFAULT_PRODUCT_FAMILY</c>).</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional explicit API base URL (bound from <c>Maxio:BaseUrl</c>). When set it is used verbatim
    /// as the base address, overriding the URL that would otherwise be derived from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>True when the minimum required settings to reach Maxio are present.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(ProductFamilyHandle)
        && (!string.IsNullOrWhiteSpace(BaseUrl) || !string.IsNullOrWhiteSpace(Subdomain));
}
