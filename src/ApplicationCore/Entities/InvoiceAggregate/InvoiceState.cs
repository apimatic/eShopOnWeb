namespace Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

/// <summary>
/// The lifecycle stage of a bill as eShop tracks it, independent of the free-form status string the
/// payment provider reports. This is the state eShop's own authorization and transition rules key on:
/// a bill starts <see cref="Draft"/> (raised with the provider but not yet put to the shopper), becomes
/// <see cref="Issued"/> once it has been put to the shopper, and <see cref="Withdrawn"/> once it has been
/// taken back. The provider's richer status/history is surfaced separately on read.
/// </summary>
public enum InvoiceState
{
    /// <summary>Raised with the provider but not yet put to the shopper. Still correctable.</summary>
    Draft = 0,

    /// <summary>Put to the shopper; payable, and a way to pay it can be handed out.</summary>
    Issued = 1,

    /// <summary>Taken back; no longer payable and the way to pay it is no longer handed out.</summary>
    Withdrawn = 2
}
