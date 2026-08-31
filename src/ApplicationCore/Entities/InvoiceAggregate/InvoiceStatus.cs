namespace Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

/// <summary>
/// The lifecycle stage of a customer invoice as tracked by eShopOnWeb. This mirrors, but is
/// deliberately coarser than, the provider's own status: eShop only distinguishes the three
/// transitions it drives (raise, issue, withdraw). The authoritative, fine-grained state
/// (DRAFT/CREATED/SENT/PARTIAL/PAID/CANCELED) is owned by the provider and read back on demand.
/// </summary>
public enum InvoiceStatus
{
    /// <summary>The bill has been raised with the provider but not yet put to the shopper.</summary>
    Draft = 0,

    /// <summary>The bill has been put to the shopper and can be paid.</summary>
    Issued = 1,

    /// <summary>The bill has been withdrawn and can no longer be paid.</summary>
    Withdrawn = 2
}
