namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// The handful of site-level facts this integration depends on.
/// </summary>
/// <param name="Currency">The site's primary currency. Maxio's product model carries no currency at all, so
/// this is where plan currency comes from.</param>
/// <param name="RelationshipInvoicingEnabled">Selects which payment collection methods the site accepts.</param>
/// <param name="DefaultPaymentCollectionMethod">The site's own default, as a raw string — deliberately not
/// the SDK enum, because the provider reports it as free text.</param>
/// <param name="IsTestSite">True for a sandbox site.</param>
internal sealed record MaxioSiteInfo(
    string? Currency,
    bool RelationshipInvoicingEnabled,
    string? DefaultPaymentCollectionMethod,
    string? Subdomain,
    bool? IsTestSite);
