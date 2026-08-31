namespace Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

/// <summary>
/// The invoice lifecycle as eShopOnWeb tracks it locally. This is deliberately
/// coarser than the payment provider's own status: it captures the transitions
/// the application controls (raise, issue, withdraw) and is used to authorize
/// what a caller may still do to a bill, independent of the richer status the
/// provider reports back (which is cached separately on the invoice).
/// </summary>
public enum InvoiceStatus
{
    /// <summary>Raised with the provider but not yet put to the shopper.</summary>
    Draft = 0,

    /// <summary>Put to the shopper; a payment link can now be handed out.</summary>
    Issued = 1,

    /// <summary>Withdrawn; no longer payable and the payment link is no longer handed out.</summary>
    Withdrawn = 2
}
