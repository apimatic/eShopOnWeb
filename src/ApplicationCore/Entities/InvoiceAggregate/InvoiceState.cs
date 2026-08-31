namespace Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

/// <summary>
/// The lifecycle state of a bill as this application tracks it. This is authoritative for what the
/// application will allow (correcting, issuing, withdrawing) and is deliberately distinct from the
/// free-text status string the provider reports — the provider owns its own status vocabulary.
/// </summary>
public enum InvoiceState
{
    /// <summary>Raised with the provider but not yet put to the shopper. Still correctable.</summary>
    Draft = 0,

    /// <summary>Put to the shopper. A payment link may now be handed out.</summary>
    Issued = 1,

    /// <summary>Withdrawn. No longer payable and no payment link is handed out.</summary>
    Withdrawn = 2
}
