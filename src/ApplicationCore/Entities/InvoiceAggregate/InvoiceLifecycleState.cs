namespace Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

/// <summary>
/// The lifecycle stage of a bill as tracked by eShopOnWeb. This is distinct from the richer
/// status the provider owns (e.g. DRAFT/SENT/PARTIAL/PAID/CANCELED) which is surfaced separately;
/// this local state is what governs which caller actions are still allowed.
/// </summary>
public enum InvoiceLifecycleState
{
    /// <summary>Raised with the provider but not yet put to the shopper. Still correctable.</summary>
    Draft = 0,

    /// <summary>Put to the shopper. A payment link can be handed out; it can no longer be corrected.</summary>
    Issued = 1,

    /// <summary>Withdrawn. No longer payable and can no longer be corrected.</summary>
    Withdrawn = 2
}
