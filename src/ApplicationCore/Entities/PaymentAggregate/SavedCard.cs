using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A card the shopper saved with the payment provider's vault. Only the provider's token
/// and display data (brand, last four digits, expiry, holder name) are stored - never the
/// full card number or security code.
/// </summary>
public class SavedCard : BaseEntity, IAggregateRoot
{
    public string BuyerId { get; private set; } = string.Empty;

    /// <summary>The vault payment token id issued by the provider.</summary>
    public string VaultTokenId { get; private set; } = string.Empty;

    /// <summary>The provider's customer id associated with the token at vault time.</summary>
    public string? PayPalCustomerId { get; private set; }

    public string Brand { get; private set; } = string.Empty;
    public string LastFourDigits { get; private set; } = string.Empty;

    /// <summary>Expiry as reported by the provider, e.g. "2027-01".</summary>
    public string? Expiry { get; private set; }

    public string? CardholderName { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public SavedCard(string buyerId, string vaultTokenId, string? payPalCustomerId,
        string brand, string lastFourDigits, string? expiry, string? cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(vaultTokenId, nameof(vaultTokenId));
        Guard.Against.NullOrEmpty(brand, nameof(brand));
        Guard.Against.NullOrEmpty(lastFourDigits, nameof(lastFourDigits));
        BuyerId = buyerId;
        VaultTokenId = vaultTokenId;
        PayPalCustomerId = payPalCustomerId;
        Brand = brand;
        LastFourDigits = lastFourDigits;
        Expiry = expiry;
        CardholderName = cardholderName;
        CreatedAt = DateTimeOffset.UtcNow;
    }

#pragma warning disable CS8618 // Required by Entity Framework
    private SavedCard() { }
}
