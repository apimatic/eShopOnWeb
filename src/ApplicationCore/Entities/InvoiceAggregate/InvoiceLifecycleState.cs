namespace Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

/// <summary>
/// eShop's own view of where a bill stands, kept independent of the exact status string the
/// provider reports. The provider's status machine has quirks (for example, correcting a bill
/// auto-publishes it), so eShop owns the authoritative notion of whether a bill has been put to
/// the shopper (<see cref="Issued"/>) or taken back (<see cref="Withdrawn"/>).
/// </summary>
public enum InvoiceLifecycleState
{
    /// <summary>Raised against the order and held with the provider, not yet put to the shopper.</summary>
    Raised = 0,

    /// <summary>Put to the shopper; a way to pay it can now be handed out.</summary>
    Issued = 1,

    /// <summary>Withdrawn; no longer payable and the way to pay it is no longer handed out.</summary>
    Withdrawn = 2
}
