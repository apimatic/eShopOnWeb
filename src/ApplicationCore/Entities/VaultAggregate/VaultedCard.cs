using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.VaultAggregate;

/// <summary>
/// A card a shopper saved for reuse. The card itself lives in PayPal's vault; this app keeps only
/// the PayPal vault token and a safe descriptor (brand + last 4 + expiry) — never the full number.
/// </summary>
public class VaultedCard : BaseEntity, IAggregateRoot
{
    /// <summary>Owner of the saved card (the buyer identity, i.e. the user name from the token).</summary>
    public string BuyerId { get; private set; }

    /// <summary>The PayPal vault payment-token id used to charge the card later.</summary>
    public string VaultId { get; private set; }

    public string? Brand { get; private set; }
    public string? Last4 { get; private set; }

    /// <summary>Card expiry as PayPal reports it (YYYY-MM), safe to show.</summary>
    public string? Expiry { get; private set; }

    /// <summary>Optional shopper-friendly label.</summary>
    public string? Label { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

#pragma warning disable CS8618 // Required by Entity Framework
    private VaultedCard() { }
#pragma warning restore CS8618

    public VaultedCard(string buyerId, string vaultId, string? brand, string? last4, string? expiry, string? label)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(vaultId, nameof(vaultId));

        BuyerId = buyerId;
        VaultId = vaultId;
        Brand = brand;
        Last4 = last4;
        Expiry = expiry;
        Label = label;
    }
}
