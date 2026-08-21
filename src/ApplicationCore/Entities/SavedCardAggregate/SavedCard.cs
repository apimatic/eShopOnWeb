using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;

/// <summary>
/// A card a shopper vaulted with PayPal for reuse. The application stores only the PayPal vault
/// token and a safe descriptor (brand, last four, expiry) — never the full card number.
/// A saved card belongs to the shopper who saved it.
/// </summary>
public class SavedCard : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SavedCard() { }

    public SavedCard(string ownerId, string payPalVaultId, string? cardBrand, string? lastFourDigits, string? expiry)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(payPalVaultId, nameof(payPalVaultId));

        OwnerId = ownerId;
        PayPalVaultId = payPalVaultId;
        CardBrand = cardBrand;
        LastFourDigits = lastFourDigits;
        Expiry = expiry;
        CreatedDate = DateTimeOffset.Now;
    }

    /// <summary>The owning shopper's identity.</summary>
    public string OwnerId { get; private set; }

    /// <summary>The PayPal vault token id used to pay with this card.</summary>
    public string PayPalVaultId { get; private set; }

    public string? CardBrand { get; private set; }
    public string? LastFourDigits { get; private set; }
    public string? Expiry { get; private set; }
    public DateTimeOffset CreatedDate { get; private set; }
}
