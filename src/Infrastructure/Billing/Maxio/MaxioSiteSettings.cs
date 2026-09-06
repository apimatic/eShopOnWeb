namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// The handful of Maxio site-level settings this integration depends on, read once and cached.
/// </summary>
/// <param name="Currency">Site currency. Maxio's product model carries no currency of its own.</param>
/// <param name="RelationshipInvoicingEnabled">
/// Which invoicing architecture the site runs, which decides the valid payment-collection methods.
/// </param>
/// <param name="DefaultPaymentCollectionMethod">The site's own default, kept for diagnostics.</param>
internal record MaxioSiteSettings(
    string? Currency,
    bool RelationshipInvoicingEnabled,
    string? DefaultPaymentCollectionMethod);
