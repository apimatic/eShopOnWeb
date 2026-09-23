using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A card a shopper saved (vaulted at PayPal) for reuse. The application stores only the PayPal vault id
/// and a safe descriptor (brand + last digits + expiry) — never the card number, CVV, or any full detail.
/// Belongs to the shopper who saved it.
/// </summary>
public class SavedCard : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SavedCard() { }

    public SavedCard(string buyerId, string payPalVaultId, string? brand, string? lastDigits,
        string? expiry, string? cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalVaultId, nameof(payPalVaultId));

        BuyerId = buyerId;
        PayPalVaultId = payPalVaultId;
        Brand = brand;
        LastDigits = lastDigits;
        Expiry = expiry;
        CardholderName = cardholderName;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The shopper who saved this card.</summary>
    public string BuyerId { get; private set; }

    /// <summary>The PayPal-generated vault id used to pay with this card later.</summary>
    public string PayPalVaultId { get; private set; }

    public string? Brand { get; private set; }

    /// <summary>The last digits of the card, safe to show the shopper.</summary>
    public string? LastDigits { get; private set; }

    /// <summary>Expiry in YYYY-MM, safe to show the shopper.</summary>
    public string? Expiry { get; private set; }

    public string? CardholderName { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
