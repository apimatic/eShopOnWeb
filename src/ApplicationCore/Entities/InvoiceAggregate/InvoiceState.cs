namespace Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

/// <summary>
/// The lifecycle state of a bill as this application understands it. It is controlled by our own
/// transitions (raise -> Draft, issue -> Issued, withdraw -> Withdrawn) and is deliberately kept
/// independent of the free-form status string the provider reports, whose exact values are not part
/// of the provider's published contract.
/// </summary>
public enum InvoiceState
{
    /// <summary>Raised with the provider but not yet put to the shopper. Correctable; not payable.</summary>
    Draft = 0,

    /// <summary>Put to the shopper. A payment link may be handed out; no longer correctable.</summary>
    Issued = 1,

    /// <summary>Withdrawn. No longer payable and no longer correctable.</summary>
    Withdrawn = 2
}
