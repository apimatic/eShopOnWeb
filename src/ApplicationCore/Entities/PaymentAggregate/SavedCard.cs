using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A card a shopper saved (vaulted at PayPal) for reuse. This app stores only the PayPal vault token and a
/// safe descriptor (brand + last four + expiry) — never the full card number or CVV.
/// A saved card belongs to the shopper who saved it.
/// </summary>
public class SavedCard : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SavedCard() { }

    public SavedCard(string buyerId, string vaultTokenId, string cardBrand, string lastFourDigits, string? expiry)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(vaultTokenId, nameof(vaultTokenId));

        BuyerId = buyerId;
        VaultTokenId = vaultTokenId;
        CardBrand = cardBrand;
        LastFourDigits = lastFourDigits;
        Expiry = expiry;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Owner of this saved card (the shopper's identity / email).</summary>
    public string BuyerId { get; private set; }

    /// <summary>The PayPal vault payment-method token id used to charge this card.</summary>
    public string VaultTokenId { get; private set; }

    public string CardBrand { get; private set; }
    public string LastFourDigits { get; private set; }
    public string? Expiry { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
