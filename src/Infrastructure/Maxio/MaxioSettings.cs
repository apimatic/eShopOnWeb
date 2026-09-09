namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Configuration for the Maxio Advanced Billing integration, bound from the "Maxio" configuration
/// section. Values are supplied at deployment time (user secrets / environment variables) and are
/// never hard-coded: Maxio:ApiKey (MAXIO_API_KEY), Maxio:Subdomain (MAXIO_SITE_SUBDOMAIN),
/// Maxio:Environment (MAXIO_ENVIRONMENT), Maxio:ProductFamilyHandle (MAXIO_DEFAULT_PRODUCT_FAMILY)
/// and the optional Maxio:BaseUrl override.
/// </summary>
public sealed class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio API key (Basic-auth username). Required.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Maxio site subdomain, e.g. "my-site" for https://my-site.chargify.com. Required.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Hosting environment: "us" (default) or "eu". Sandbox vs live is decided by the API key and subdomain.</summary>
    public string Environment { get; set; } = "us";

    /// <summary>Handle of the product family that holds the subscription plans. Required.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>Optional verbatim base-URL override (e.g. for a proxy or mock host). When set, it replaces the derived https://{subdomain}.chargify.com address.</summary>
    public string? BaseUrl { get; set; }
}
