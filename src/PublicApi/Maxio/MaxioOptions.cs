namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Settings that configure the Maxio Advanced Billing integration.
/// Values are bound at runtime from the <c>Maxio:</c> configuration section and are
/// never committed to the repository.
/// </summary>
public sealed class MaxioOptions
{
    public const string CONFIG_NAME = "Maxio";

    /// <summary>Basic-auth API key (bound from MAXIO_API_KEY).</summary>
    public string? ApiKey { get; set; }

    /// <summary>Maxio site subdomain (bound from MAXIO_SITE_SUBDOMAIN).</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family that holds the subscription plans (bound from MAXIO_DEFAULT_PRODUCT_FAMILY).</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional override for the API base address. When set it is used verbatim instead
    /// of deriving <c>https://{subdomain}.chargify.com</c> from the subdomain.
    /// </summary>
    public string? BaseUrl { get; set; }
}
