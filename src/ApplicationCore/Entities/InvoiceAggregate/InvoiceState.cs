namespace Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

/// <summary>
/// The lifecycle stage of a bill from eShop's point of view. This is eShop's own record of what it
/// has done to the bill; the payment provider remains authoritative for the settlement state, which
/// is mirrored separately on <see cref="Invoice.ProviderStatus"/>.
/// </summary>
public enum InvoiceState
{
    /// <summary>Raised against an order but not yet put to the shopper. Still correctable.</summary>
    Draft = 0,

    /// <summary>Put to the shopper (published with the provider). A payment link can now be handed out.</summary>
    Issued = 1,

    /// <summary>Withdrawn (cancelled with the provider). No longer payable.</summary>
    Withdrawn = 2
}
