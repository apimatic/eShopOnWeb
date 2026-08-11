using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A card a shopper has saved for reuse. The application never stores full card details — only
/// the PayPal vault token that stands in for the card, plus a safe description (brand and last
/// four digits) so the shopper can recognise which card it is. A saved card belongs to the
/// shopper who saved it and is never seen, used, or deleted by anyone else.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }

    public SavedPaymentMethod(string buyerId, string vaultId, string cardBrand, string lastFourDigits,
        string? cardExpiry, string? cardholderName, string? payPalCustomerId)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(vaultId, nameof(vaultId));
        Guard.Against.NullOrEmpty(cardBrand, nameof(cardBrand));
        Guard.Against.NullOrEmpty(lastFourDigits, nameof(lastFourDigits));

        BuyerId = buyerId;
        VaultId = vaultId;
        CardBrand = cardBrand;
        LastFourDigits = lastFourDigits;
        CardExpiry = cardExpiry;
        CardholderName = cardholderName;
        PayPalCustomerId = payPalCustomerId;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The shopper who saved the card (username). The sole owner.</summary>
    public string BuyerId { get; private set; }

    /// <summary>The PayPal vault (payment token) id. This is what is used to pay; no card number is kept.</summary>
    public string VaultId { get; private set; }

    /// <summary>The PayPal customer id the vaulted card is linked to, used to group a shopper's cards.</summary>
    public string? PayPalCustomerId { get; private set; }

    public string CardBrand { get; private set; }

    /// <summary>The last four digits only — enough to recognise the card, never the full number.</summary>
    public string LastFourDigits { get; private set; }

    /// <summary>Card expiry (YYYY-MM) as returned by PayPal, for display.</summary>
    public string? CardExpiry { get; private set; }

    public string? CardholderName { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
