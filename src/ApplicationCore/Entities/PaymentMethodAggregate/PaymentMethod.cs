using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

/// <summary>
/// A card a shopper saved for reuse. The card itself lives in PayPal's vault; this app only keeps the
/// reusable vault token id and a safe descriptor (brand, last four, expiry) — never the full card
/// number. A saved card belongs to the shopper who saved it.
/// </summary>
public class PaymentMethod : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() { }

    public PaymentMethod(string buyerId, string payPalVaultId, string cardBrand, string lastFourDigits, string expiry)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalVaultId, nameof(payPalVaultId));

        BuyerId = buyerId;
        PayPalVaultId = payPalVaultId;
        CardBrand = cardBrand;
        LastFourDigits = lastFourDigits;
        Expiry = expiry;
    }

    /// <summary>The owning shopper (username/email).</summary>
    public string BuyerId { get; private set; }

    /// <summary>The reusable PayPal vault/payment-token id used to pay with this card.</summary>
    public string PayPalVaultId { get; private set; }

    /// <summary>Safe descriptor: the card network (e.g. VISA).</summary>
    public string CardBrand { get; private set; }

    /// <summary>Safe descriptor: the last four digits.</summary>
    public string LastFourDigits { get; private set; }

    /// <summary>Safe descriptor: expiry as reported by PayPal (YYYY-MM).</summary>
    public string Expiry { get; private set; }
}
