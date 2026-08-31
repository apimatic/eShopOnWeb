namespace Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

/// <summary>
/// The lifecycle stage of a bill as eShop tracks it locally. This is distinct from — and
/// coarser than — the raw status string the provider reports; it is what the app's own rules
/// (who may correct, issue or withdraw a bill) are enforced against.
/// </summary>
public enum InvoiceStatus
{
    /// <summary>Raised with the provider but not yet put to the shopper.</summary>
    Draft = 0,

    /// <summary>Put to the shopper; a way to pay it can be handed out.</summary>
    Issued = 1,

    /// <summary>Taken back; no longer payable and no pay link is handed out.</summary>
    Withdrawn = 2
}
