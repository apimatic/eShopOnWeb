namespace Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

/// <summary>
/// The lifecycle of a bill as tracked locally by eShop. The billing provider (Visa/CyberSource)
/// owns the authoritative status string; this local status records the transitions eShop has
/// driven so the API can answer deterministically which corrections are still allowed and whether
/// a bill is still payable, without a round-trip to the provider.
/// </summary>
public enum InvoiceStatus
{
    /// <summary>Raised with the provider but not yet put to the shopper. Still correctable.</summary>
    Draft = 0,

    /// <summary>Put to the shopper (issued/delivered). A payment link can be handed out.</summary>
    Issued = 1,

    /// <summary>Withdrawn. No longer payable and no payment link is handed out.</summary>
    Withdrawn = 2
}
