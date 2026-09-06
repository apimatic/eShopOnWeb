using AdvancedBilling.Standard.Models;

namespace Microsoft.eShopWeb.Infrastructure.Subscriptions.Maxio;

/// <summary>
/// The handful of site-level facts the integration needs from Maxio: which currency prices are quoted
/// in, and which billing architecture the site runs, which decides how a subscription collects payment.
/// </summary>
/// <param name="Currency">ISO 4217 code of the site's primary currency.</param>
/// <param name="RelationshipInvoicingEnabled">True when the site runs Relationship Invoicing.</param>
public record MaxioSite(string Currency, bool RelationshipInvoicingEnabled)
{
    /// <summary>Fallback used when the site's currency cannot be read.</summary>
    public const string DefaultCurrency = "USD";

    /// <summary>
    /// The collection method to use when the shopper has no payment method on file. Both values bill the
    /// subscription by invoice rather than charging a card, which is what allows signup without card
    /// capture (and therefore without a 3-D Secure detour). Maxio names it <c>remittance</c> under
    /// Relationship Invoicing and <c>invoice</c> under the legacy Statements architecture.
    /// </summary>
    public CollectionMethod InvoicedCollectionMethod =>
        RelationshipInvoicingEnabled ? CollectionMethod.Remittance : CollectionMethod.Invoice;
}
