using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;

/// <summary>
/// A card the shopper vaulted with PayPal for reuse. Only safe display data is
/// kept locally (brand, last digits, expiry); the full PAN never enters this
/// database — PayPal's vault holds it under <see cref="VaultTokenId"/>.
/// </summary>
public class SavedCard : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SavedCard() {}

    public SavedCard(string buyerId, string payPalCustomerId, string vaultTokenId,
        string? brand, string? lastDigits, string? expiry, string? cardholderName)
    {
        BuyerId = Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        PayPalCustomerId = Guard.Against.NullOrEmpty(payPalCustomerId, nameof(payPalCustomerId));
        VaultTokenId = Guard.Against.NullOrEmpty(vaultTokenId, nameof(vaultTokenId));
        Brand = brand;
        LastDigits = lastDigits;
        Expiry = expiry;
        CardholderName = cardholderName;
    }

    public string BuyerId { get; private set; }
    public string PayPalCustomerId { get; private set; }
    public string VaultTokenId { get; private set; }
    public string? Brand { get; private set; }
    public string? LastDigits { get; private set; }
    /// <summary>Card expiry in PayPal's YYYY-MM format.</summary>
    public string? Expiry { get; private set; }
    public string? CardholderName { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
}
