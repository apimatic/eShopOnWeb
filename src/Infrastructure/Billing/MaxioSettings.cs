namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Strongly-typed view of the <c>Maxio:</c> configuration section. Values are supplied at runtime
/// (user-secrets in development, environment/keyvault in production) — never hard-coded, so the same
/// build targets any Maxio site and catalog.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio/Chargify site API key (used as the HTTP Basic username). Bound from <c>Maxio:ApiKey</c>.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Site subdomain, e.g. <c>my-site</c>. Bound from <c>Maxio:Subdomain</c>.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family whose plans are exposed. Bound from <c>Maxio:ProductFamilyHandle</c>.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional explicit API base URL. When set it is used verbatim, overriding the URL derived from
    /// <see cref="Subdomain"/>/<see cref="Environment"/>. Bound from <c>Maxio:BaseUrl</c>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Hosting region: <c>US</c> (default) or <c>EU</c>. Bound from <c>Maxio:Environment</c> when present.</summary>
    public string Environment { get; set; } = "US";

    /// <summary>
    /// How the subscription's balance is collected: <c>remittance</c> (invoice, Relationship Invoicing —
    /// the default), <c>invoice</c> (legacy Statements), <c>automatic</c>, or <c>prepaid</c>. Using an
    /// invoice-based method lets a shopper subscribe without a payment method on file. Bound from
    /// <c>Maxio:PaymentCollectionMethod</c> when present.
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";
}
