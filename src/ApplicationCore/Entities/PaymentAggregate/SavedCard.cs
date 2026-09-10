using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A card a shopper has saved (vaulted) for reuse. The application never stores the full card number;
/// it keeps only PayPal's vault token plus a safe description (brand, last four, expiry) so the shopper
/// can recognise which card it is. A saved card belongs to exactly one buyer.
/// </summary>
public class SavedCard : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SavedCard() { }

    public SavedCard(string buyerId, string vaultId, string payPalCustomerId, string? brand, string? last4, string? expiry, string? cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(vaultId, nameof(vaultId));
        Guard.Against.NullOrEmpty(payPalCustomerId, nameof(payPalCustomerId));

        BuyerId = buyerId;
        VaultId = vaultId;
        PayPalCustomerId = payPalCustomerId;
        Brand = brand;
        Last4 = last4;
        Expiry = expiry;
        CardholderName = cardholderName;
    }

    public string BuyerId { get; private set; }

    /// <summary>PayPal vault payment-token id; used as the payment source when paying with this card.</summary>
    public string VaultId { get; private set; }

    /// <summary>The merchant-scoped PayPal customer id the vaulted card is filed under.</summary>
    public string PayPalCustomerId { get; private set; }

    public string? Brand { get; private set; }
    public string? Last4 { get; private set; }

    /// <summary>Card expiry in ISO-8601 YYYY-MM form (no full number is ever stored).</summary>
    public string? Expiry { get; private set; }
    public string? CardholderName { get; private set; }
}
