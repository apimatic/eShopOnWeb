namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// The handful of site-wide facts the integration needs, read once and cached.
/// </summary>
/// <param name="Currency">ISO currency the site prices in. Products carry no currency of their own.</param>
/// <param name="RelationshipInvoicingEnabled">
/// Which billing architecture the site runs. It decides which payment collection methods are even valid:
/// Relationship Invoicing accepts <c>remittance</c>, the legacy Statements architecture accepts
/// <c>invoice</c>, and the two are not interchangeable.
/// </param>
/// <param name="DefaultPaymentCollectionMethod">
/// The collection method applied when a subscription does not set one. A bare string on the response side,
/// even though the request side takes an enum.
/// </param>
internal sealed record MaxioSiteInfo(
    string? Currency,
    bool? RelationshipInvoicingEnabled,
    string? DefaultPaymentCollectionMethod);
