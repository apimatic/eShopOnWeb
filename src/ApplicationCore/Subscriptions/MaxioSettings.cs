namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Strongly-typed configuration for the Maxio Advanced Billing integration.
/// Bound from the <c>Maxio</c> configuration section. Values are supplied via .NET user-secrets
/// (development) or environment/secret-store configuration (production) — never committed to the repo.
/// </summary>
public class MaxioSettings
{
    /// <summary>The configuration section name: <c>Maxio</c>.</summary>
    public const string SectionName = "Maxio";

    /// <summary>Maxio (Chargify) API key. Used as the HTTP Basic username (password is the literal "x").</summary>
    public string? ApiKey { get; set; }

    /// <summary>The Maxio site subdomain, e.g. <c>my-site</c> → <c>https://my-site.chargify.com</c>.</summary>
    public string? Subdomain { get; set; }

    /// <summary>The API handle of the product family whose products are offered as subscription plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional explicit base URL override. When set, it is used verbatim as the API base address instead
    /// of deriving one from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Payment collection method for new subscriptions. Plans that require no payment method are enrolled on
    /// an invoice basis so no card capture is needed. Valid values depend on the site's billing architecture:
    /// <c>remittance</c> (Relationship Invoicing — the default) or <c>invoice</c> (legacy Statements);
    /// <c>automatic</c> would attempt to charge a card at signup. Optional; defaults to <c>remittance</c>.
    /// </summary>
    public string? PaymentCollectionMethod { get; set; }
}
