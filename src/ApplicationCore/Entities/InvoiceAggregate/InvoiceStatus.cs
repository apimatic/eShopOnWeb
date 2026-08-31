namespace Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

/// <summary>
/// The lifecycle state eShopOnWeb tracks locally for a bill raised against an order.
/// This mirrors the transitions the application drives with the provider; the authoritative,
/// provider-owned status is read live from the provider on retrieval.
/// </summary>
public enum InvoiceStatus
{
    /// <summary>Raised with the provider but not yet put to the shopper. Correctable.</summary>
    Draft = 0,

    /// <summary>Put to the shopper (issued/delivered). Payable; a payment link is available.</summary>
    Issued = 1,

    /// <summary>Withdrawn (cancelled). No longer payable; no payment link is handed out.</summary>
    Withdrawn = 2
}
