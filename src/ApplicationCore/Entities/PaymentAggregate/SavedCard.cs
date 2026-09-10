using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A card a shopper has saved for reuse. Stores only the PayPal vault id plus a safe description
/// (brand + last four + expiry) — never the full card number or CVV. A saved card belongs to the
/// shopper who saved it.
/// </summary>
public class SavedCard : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private SavedCard() { }
#pragma warning restore CS8618

    public SavedCard(string buyerId, string payPalVaultId, string? payPalCustomerId,
        string brand, string lastDigits, string? expiry, string? cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalVaultId, nameof(payPalVaultId));

        BuyerId = buyerId;
        PayPalVaultId = payPalVaultId;
        PayPalCustomerId = payPalCustomerId;
        Brand = brand;
        LastDigits = lastDigits;
        Expiry = expiry;
        CardholderName = cardholderName;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The shopper who saved this card. Used to scope shopper access.</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal vault (payment token) id used to charge the card later.</summary>
    public string PayPalVaultId { get; private set; }

    /// <summary>PayPal customer id the vaulted card belongs to (shared across a shopper's cards).</summary>
    public string? PayPalCustomerId { get; private set; }

    public string Brand { get; private set; }
    public string LastDigits { get; private set; }
    public string? Expiry { get; private set; }
    public string? CardholderName { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
