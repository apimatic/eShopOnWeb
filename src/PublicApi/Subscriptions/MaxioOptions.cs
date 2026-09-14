namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// Settings bound from the "Maxio" configuration section.
/// Values are loaded from environment variables / user secrets; nothing is hard-coded here.
/// </summary>
public sealed class MaxioOptions
{
    public const string CONFIG_NAME = "Maxio";

    /// <summary>Maxio Advanced Billing API key (env MAXIO_API_KEY).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Maxio site subdomain, e.g. "cp-exp-5" (env MAXIO_SITE_SUBDOMAIN).</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family that holds the subscribable plans (env MAXIO_DEFAULT_PRODUCT_FAMILY).</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override used verbatim as the API base address. When set it replaces the
    /// https://{site}.chargify.com template entirely; the Subdomain is then ignored for addressing.
    /// </summary>
    public string? BaseUrl { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(Subdomain)
        && !string.IsNullOrWhiteSpace(ProductFamilyHandle);
}
