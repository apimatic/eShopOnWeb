using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

/// <summary>
/// A card a shopper has saved (vaulted at PayPal) for reuse on later orders. The application stores
/// only the PayPal vault token id plus a safe description (brand + last four digits + expiry) — never
/// the full card details. A saved card belongs to exactly one shopper (<see cref="BuyerId"/>).
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }
#pragma warning restore CS8618

    public SavedPaymentMethod(string buyerId, string payPalVaultTokenId, string cardBrand,
        string lastFourDigits, string? expiry)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalVaultTokenId, nameof(payPalVaultTokenId));

        BuyerId = buyerId;
        PayPalVaultTokenId = payPalVaultTokenId;
        CardBrand = cardBrand;
        LastFourDigits = lastFourDigits;
        Expiry = expiry;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The shopper who owns this saved card (the JWT identity / order BuyerId).</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal-generated vault token id — passed as the payment source when reusing the card.</summary>
    public string PayPalVaultTokenId { get; private set; }

    public string CardBrand { get; private set; }
    public string LastFourDigits { get; private set; }
    public string? Expiry { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
