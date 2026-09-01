using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

/// <summary>
/// A card vaulted with PayPal on behalf of a shopper. Only safe display data
/// (brand, last digits, expiry) is stored here — never the full card number or CVC.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    private SavedPaymentMethod() { }

    public SavedPaymentMethod(string buyerId, string vaultTokenId, string brand, string lastDigits,
        string expiry, string? cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(vaultTokenId, nameof(vaultTokenId));

        BuyerId = buyerId;
        VaultTokenId = vaultTokenId;
        Brand = brand;
        LastDigits = lastDigits;
        Expiry = expiry;
        CardholderName = cardholderName;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string BuyerId { get; private set; } = string.Empty;
    public string VaultTokenId { get; private set; } = string.Empty;
    public string Brand { get; private set; } = string.Empty;
    public string LastDigits { get; private set; } = string.Empty;
    public string Expiry { get; private set; } = string.Empty;
    public string? CardholderName { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
