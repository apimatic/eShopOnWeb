using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;

/// <summary>
/// A card a shopper has saved for reuse. The application database holds only PayPal's vault id plus
/// safe display data (brand, last 4, expiry) — never the full card number. A saved card belongs to the
/// shopper who saved it; ownership is enforced through <see cref="BuyerId"/>.
/// </summary>
public class SavedCard : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SavedCard() { }

    public SavedCard(string buyerId, string payPalVaultId, string cardBrand, string last4, string expiry,
        string? cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalVaultId, nameof(payPalVaultId));

        BuyerId = buyerId;
        PayPalVaultId = payPalVaultId;
        CardBrand = cardBrand;
        Last4 = last4;
        Expiry = expiry;
        CardholderName = cardholderName;
    }

    /// <summary>Owner identity (the token's user name / email).</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal vault (payment token) id used to charge this card later. Not card data.</summary>
    public string PayPalVaultId { get; private set; }

    public string CardBrand { get; private set; }
    public string Last4 { get; private set; }
    public string Expiry { get; private set; }
    public string? CardholderName { get; private set; }
}
