namespace Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

/// <summary>
/// The eShop-owned lifecycle of a bill. This is deliberately authoritative for what eShop allows a
/// caller to do (correct/issue/withdraw) and for whether a payment link may be handed out — it does NOT
/// mirror the provider's own status vocabulary, which is a free-form string the provider owns
/// (see <see cref="Invoice.ProviderStatus"/>) and whose exact values are not part of this SDK's contract.
/// </summary>
public enum InvoiceStatus
{
    /// <summary>Raised with the provider but not yet put to the shopper. Correctable; not payable.</summary>
    Draft = 0,

    /// <summary>Put to the shopper. A payment link can be handed out; no longer correctable.</summary>
    Issued = 1,

    /// <summary>Withdrawn. No longer payable and no payment link is handed out; no longer correctable.</summary>
    Withdrawn = 2
}
