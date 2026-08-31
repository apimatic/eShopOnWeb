namespace Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

/// <summary>
/// The local lifecycle of a bill as this application understands it. This is intentionally a small
/// set of states that eShop owns; the richer set of states owned by the provider (DRAFT, CREATED,
/// SENT, PARTIAL, PAID, CANCELED, ...) is read back from the provider on demand and is not mirrored
/// one-to-one here.
/// </summary>
public enum InvoiceStatus
{
    /// <summary>The bill has been raised with the provider but has not yet been put to the shopper.</summary>
    Draft = 0,

    /// <summary>The bill has been put to the shopper and can now be paid.</summary>
    Issued = 1,

    /// <summary>The bill has been withdrawn and must no longer be payable.</summary>
    Withdrawn = 2
}
