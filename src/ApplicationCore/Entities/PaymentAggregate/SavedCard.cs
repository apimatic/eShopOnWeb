using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A card a shopper has saved (vaulted at PayPal) for reuse. Never holds full card details —
/// only a safe descriptor (brand, last four, expiry) plus the PayPal vault + customer ids.
/// Belongs to the shopper who saved it (<see cref="BuyerId"/>).
/// </summary>
public class SavedCard : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SavedCard() { }
    #pragma warning restore CS8618

    public SavedCard(string buyerId, string payPalCustomerId, string vaultId,
        string? brand, string? last4, string? expiry, string? alias)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalCustomerId, nameof(payPalCustomerId));
        Guard.Against.NullOrEmpty(vaultId, nameof(vaultId));

        BuyerId = buyerId;
        PayPalCustomerId = payPalCustomerId;
        VaultId = vaultId;
        Brand = brand;
        Last4 = last4;
        Expiry = expiry;
        Alias = alias;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string BuyerId { get; private set; }

    /// <summary>The PayPal-generated customer id this shopper's vaulted cards are attached to.</summary>
    public string PayPalCustomerId { get; private set; }

    /// <summary>The PayPal vault (payment token) id used to charge this card.</summary>
    public string VaultId { get; private set; }

    public string? Brand { get; private set; }
    public string? Last4 { get; private set; }
    public string? Expiry { get; private set; }
    public string? Alias { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
