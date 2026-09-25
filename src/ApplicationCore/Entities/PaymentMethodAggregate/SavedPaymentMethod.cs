using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

/// <summary>
/// A card a shopper has saved (vaulted) with PayPal for reuse. Belongs to the shopper who saved
/// it (<see cref="BuyerId"/>). Holds only the PayPal vault token id and a safe description
/// (brand + last four digits + expiry) — never a full card number, which is vaulted with PayPal.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }

    public SavedPaymentMethod(string buyerId, string payPalPaymentTokenId, string? brand,
        string? lastFourDigits, string? expiry, string? cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalPaymentTokenId, nameof(payPalPaymentTokenId));

        BuyerId = buyerId;
        PayPalPaymentTokenId = payPalPaymentTokenId;
        Brand = brand;
        LastFourDigits = lastFourDigits;
        Expiry = expiry;
        CardholderName = cardholderName;
    }

    /// <summary>The shopper who owns this saved card (the JWT identity name).</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal's vault payment-token id — used as card.vault_id when paying.</summary>
    public string PayPalPaymentTokenId { get; private set; }

    /// <summary>Card network/brand (e.g. VISA), for display.</summary>
    public string? Brand { get; private set; }

    /// <summary>Last four digits of the card, for display.</summary>
    public string? LastFourDigits { get; private set; }

    /// <summary>Card expiry in YYYY-MM, for display.</summary>
    public string? Expiry { get; private set; }

    /// <summary>Cardholder name, for display.</summary>
    public string? CardholderName { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
}
