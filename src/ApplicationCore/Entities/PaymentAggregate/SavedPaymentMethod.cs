using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A card a shopper saved for reuse. The app stores only PayPal's vault token id plus safe display details
/// (brand, last four digits, expiry, cardholder name) — never the card number or CVV. Owned by the shopper
/// named in <see cref="BuyerId"/>; one shopper must never see, use, or delete another's.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }

    public SavedPaymentMethod(string buyerId, string payPalVaultId, string? cardBrand, string? lastFourDigits,
        string? expiry, string? cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalVaultId, nameof(payPalVaultId));

        BuyerId = buyerId;
        PayPalVaultId = payPalVaultId;
        CardBrand = cardBrand;
        LastFourDigits = lastFourDigits;
        Expiry = expiry;
        CardholderName = cardholderName;
        CreatedDate = System.DateTimeOffset.UtcNow;
    }

    public string BuyerId { get; private set; }

    /// <summary>PayPal Vault payment-token id, used as the card's vault_id when paying.</summary>
    public string PayPalVaultId { get; private set; }

    public string? CardBrand { get; private set; }
    public string? LastFourDigits { get; private set; }
    public string? Expiry { get; private set; }
    public string? CardholderName { get; private set; }
    public System.DateTimeOffset CreatedDate { get; private set; }
}
