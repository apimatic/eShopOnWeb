using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

/// <summary>
/// A card a shopper vaulted with the payment provider for later use.
/// Only safe display data (last digits, brand, expiry) is stored here;
/// the full card details live exclusively in the provider's vault.
/// </summary>
public class SavedCard : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private SavedCard() { }

    public SavedCard(string buyerId, string vaultTokenId, string? lastDigits, string? brand, string? expiry)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(vaultTokenId, nameof(vaultTokenId));

        BuyerId = buyerId;
        VaultTokenId = vaultTokenId;
        LastDigits = lastDigits;
        Brand = brand;
        Expiry = expiry;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string BuyerId { get; private set; }

    /// <summary>The provider-side vault token id used to charge the card.</summary>
    public string VaultTokenId { get; private set; }

    public string? LastDigits { get; private set; }
    public string? Brand { get; private set; }

    /// <summary>Card expiry in yyyy-MM format.</summary>
    public string? Expiry { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
