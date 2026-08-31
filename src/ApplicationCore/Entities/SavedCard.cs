using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

/// <summary>
/// A card vaulted with PayPal by a shopper. Only safe, recognisable details
/// (brand, last digits, expiry) are kept; the PAN itself lives only in PayPal's vault.
/// </summary>
public class SavedCard : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private SavedCard() { }

    public SavedCard(string buyerId, string vaultTokenId, string? cardholderName, string? brand, string? lastDigits, string? expiry)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(vaultTokenId, nameof(vaultTokenId));

        BuyerId = buyerId;
        VaultTokenId = vaultTokenId;
        CardholderName = cardholderName;
        Brand = brand;
        LastDigits = lastDigits;
        Expiry = expiry;
    }

    public string BuyerId { get; private set; }
    public string VaultTokenId { get; private set; }
    public string? CardholderName { get; private set; }
    public string? Brand { get; private set; }
    public string? LastDigits { get; private set; }
    public string? Expiry { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
}
