namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Strongly-typed Maxio Advanced Billing configuration, bound from the <c>Maxio</c> configuration section.
/// Values are supplied by configuration (user-secrets / environment variables) and are never committed to the
/// repository. The same build must run against a different site/catalog, so nothing here has a hard-coded default.
/// </summary>
public class MaxioSettings
{
    public const string ConfigurationSection = "Maxio";

    /// <summary>Maxio Chargify API key (bound from <c>Maxio:ApiKey</c>). Used as the Basic-auth username.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Maxio site subdomain (bound from <c>Maxio:Subdomain</c>). Resolves the <c>https://{site}.chargify.com</c> base URL.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family whose products are the subscribable plans (bound from <c>Maxio:ProductFamilyHandle</c>).</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional explicit API base URL (bound from <c>Maxio:BaseUrl</c>). When set, it is used verbatim as the
    /// API base address instead of deriving one from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Payment collection method used when creating a subscription (bound from <c>Maxio:PaymentCollectionMethod</c>).
    /// Defaults to <c>remittance</c> so subscriptions can be created without a stored payment method (invoice
    /// billing). Use <c>automatic</c> only where a payment profile is captured first. Legacy sites may require
    /// <c>invoice</c> instead of <c>remittance</c>.
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";
}
