using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

/// <summary>
/// A card a shopper has saved (vaulted) with PayPal for reuse. The application database never holds
/// the card number — only the PayPal vault token id and a safe description (brand + last four + expiry)
/// so the shopper can recognise which card it is.
/// </summary>
public class SavedCard : BaseEntity, IAggregateRoot
{
    /// <summary>The shopper who owns this card (username == buyerId). One shopper's cards are never visible to another.</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal vault payment-token id, used as the payment source to pay later orders.</summary>
    public string VaultId { get; private set; }

    public string Brand { get; private set; }
    public string LastFourDigits { get; private set; }
    public string? ExpiryMonth { get; private set; }
    public string? ExpiryYear { get; private set; }
    public string? CardholderName { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

#pragma warning disable CS8618 // Required by Entity Framework
    private SavedCard() { }
#pragma warning restore CS8618

    public SavedCard(string buyerId, string vaultId, string brand, string lastFourDigits,
        string? expiryMonth, string? expiryYear, string? cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(vaultId, nameof(vaultId));

        BuyerId = buyerId;
        VaultId = vaultId;
        Brand = string.IsNullOrWhiteSpace(brand) ? "CARD" : brand;
        LastFourDigits = lastFourDigits ?? "****";
        ExpiryMonth = expiryMonth;
        ExpiryYear = expiryYear;
        CardholderName = cardholderName;
    }
}
