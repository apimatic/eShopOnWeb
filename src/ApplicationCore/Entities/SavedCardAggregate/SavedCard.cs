using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;

/// <summary>
/// A card a shopper vaulted with PayPal for reuse. The app stores only the PayPal vault token id and a safe
/// display (brand + last four + expiry) — never the PAN, CVV, or any full card detail. Belongs to the
/// shopper who saved it; ownership is enforced by <see cref="BuyerId"/>.
/// </summary>
public class SavedCard : BaseEntity, IAggregateRoot
{
    public string BuyerId { get; private set; }

    /// <summary>The PayPal-generated vault token id used to fund a future payment.</summary>
    public string PayPalVaultId { get; private set; }

    public string? Brand { get; private set; }
    public string? LastDigits { get; private set; }
    public string? Expiry { get; private set; }
    public string? CardholderName { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

#pragma warning disable CS8618 // Required by Entity Framework
    private SavedCard() { }
#pragma warning restore CS8618

    public SavedCard(string buyerId, string payPalVaultId, string? brand, string? lastDigits, string? expiry, string? cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalVaultId, nameof(payPalVaultId));

        BuyerId = buyerId;
        PayPalVaultId = payPalVaultId;
        Brand = brand;
        LastDigits = lastDigits;
        Expiry = expiry;
        CardholderName = cardholderName;
    }
}
