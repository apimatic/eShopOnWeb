using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;

/// <summary>
/// A card a shopper has saved for reuse. The card itself lives only in PayPal's vault; this record
/// keeps the vault token plus safe display details (never the PAN, expiry-day, or CVC).
/// </summary>
public class SavedCard : BaseEntity, IAggregateRoot
{
    /// <summary>The shopper (username/email) who owns this card. Enforces per-shopper isolation.</summary>
    public string BuyerId { get; private set; }

    /// <summary>The PayPal vault payment-token id used to charge the card on future orders.</summary>
    public string VaultTokenId { get; private set; }

    /// <summary>Card network (e.g. VISA), from PayPal. Safe to show.</summary>
    public string? Brand { get; private set; }

    /// <summary>Last digits of the PAN, from PayPal. Safe to show.</summary>
    public string? LastDigits { get; private set; }

    /// <summary>Expiry as YYYY-MM, from PayPal. Safe to show.</summary>
    public string? Expiry { get; private set; }

    /// <summary>Optional shopper-chosen label, e.g. "Personal Visa".</summary>
    public string? Alias { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    #pragma warning disable CS8618 // Required by Entity Framework
    private SavedCard() { }

    public SavedCard(string buyerId, string vaultTokenId, string? brand, string? lastDigits,
        string? expiry, string? alias)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(vaultTokenId, nameof(vaultTokenId));

        BuyerId = buyerId;
        VaultTokenId = vaultTokenId;
        Brand = brand;
        LastDigits = lastDigits;
        Expiry = expiry;
        Alias = alias;
    }
}
