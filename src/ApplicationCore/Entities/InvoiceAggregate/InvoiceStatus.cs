namespace Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

/// <summary>
/// The lifecycle state eShop keeps for a bill. This is eShop's own authoritative state machine —
/// the payment provider's status string is advisory and is kept separately on
/// <see cref="Invoice.ProviderStatus"/>.
/// </summary>
public enum InvoiceStatus
{
    /// <summary>Raised with the provider but not yet put to the shopper. Still correctable.</summary>
    Draft = 0,

    /// <summary>Put to the shopper. A payment link has been handed out. No longer correctable.</summary>
    Issued = 1,

    /// <summary>Withdrawn. No longer payable; the payment link is no longer handed out.</summary>
    Withdrawn = 2
}
