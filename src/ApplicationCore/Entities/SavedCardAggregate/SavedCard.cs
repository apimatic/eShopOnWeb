using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;

/// <summary>
/// A card a shopper has vaulted at PayPal for reuse. The application stores only PayPal's vault token
/// id and a safe, non-sensitive description — never the full card number, which lives only in PayPal's
/// vault. <see cref="BuyerId"/> ties the card to the shopper who saved it (ownership).
/// </summary>
public class SavedCard : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SavedCard() { }

    public SavedCard(string buyerId, string payPalVaultId, string? brand, string? lastFourDigits,
        string? expiry, string? label)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalVaultId, nameof(payPalVaultId));

        BuyerId = buyerId;
        PayPalVaultId = payPalVaultId;
        Brand = brand;
        LastFourDigits = lastFourDigits;
        Expiry = expiry;
        Label = label;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The shopper (JWT identity name) who owns this card.</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal Vault payment-token id. Use as card <c>vault_id</c> to charge the saved card.</summary>
    public string PayPalVaultId { get; private set; }

    public string? Brand { get; private set; }

    /// <summary>Last four digits, for the shopper to recognise the card. Never the full PAN.</summary>
    public string? LastFourDigits { get; private set; }

    /// <summary>Expiry in <c>YYYY-MM</c> form as reported by PayPal.</summary>
    public string? Expiry { get; private set; }

    /// <summary>Optional shopper-supplied label (e.g. "Personal Visa").</summary>
    public string? Label { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
