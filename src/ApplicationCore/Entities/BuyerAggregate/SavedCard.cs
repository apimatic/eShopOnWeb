using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

/// <summary>
/// A card the shopper vaulted with the payment provider for reuse. Only safe, display-grade
/// details are kept locally (brand, last digits, expiry) — the full PAN and CVC are sent
/// straight to the provider and never stored or logged by this application.
/// </summary>
public class SavedCard : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SavedCard() {}

    public SavedCard(string buyerId, string vaultTokenId, string? cardBrand, string? lastDigits, string? expiry, string? cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(vaultTokenId, nameof(vaultTokenId));

        BuyerId = buyerId;
        VaultTokenId = vaultTokenId;
        CardBrand = cardBrand;
        LastDigits = lastDigits;
        Expiry = expiry;
        CardholderName = cardholderName;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string BuyerId { get; private set; }

    /// <summary>The provider's vault token id used to charge this card later.</summary>
    public string VaultTokenId { get; private set; }

    public string? CardBrand { get; private set; }
    public string? LastDigits { get; private set; }

    /// <summary>Card expiry in YYYY-MM format.</summary>
    public string? Expiry { get; private set; }

    public string? CardholderName { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
