namespace Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

/// <summary>
/// The lifecycle of a bill as eShop tracks it, independent of (but reconciled against) the
/// state the payment provider owns.
/// </summary>
public enum InvoiceStatus
{
    /// <summary>Raised with the provider but not yet put in front of the shopper.</summary>
    Raised = 0,

    /// <summary>Put to the shopper; a way to pay it can now be handed out.</summary>
    Issued = 1,

    /// <summary>Withdrawn; no longer payable and the way to pay is no longer handed out.</summary>
    Withdrawn = 2
}
