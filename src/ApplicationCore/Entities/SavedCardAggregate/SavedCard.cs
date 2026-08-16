using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;

/// <summary>
/// A card a shopper saved once (Flow 2) to reuse for later orders. The card itself lives in PayPal's
/// vault — this app never stores the card number. We keep only the vault token id, PayPal's customer
/// id, and a safe descriptor (brand, last four, expiry) so the shopper can recognise the card.
/// A saved card belongs to exactly one shopper (<see cref="BuyerId"/>); scoping is enforced by
/// every query and command that touches it.
/// </summary>
public class SavedCard : BaseEntity, IAggregateRoot
{
    /// <summary>Owning shopper's identity (the token's name claim), same key orders use.</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal vault payment-token id used to charge the card later.</summary>
    public string VaultTokenId { get; private set; }

    /// <summary>PayPal customer id the vault token belongs to.</summary>
    public string? PayPalCustomerId { get; private set; }

    public string Brand { get; private set; }
    public string Last4 { get; private set; }
    public string Expiry { get; private set; }

    /// <summary>Optional shopper-friendly label, e.g. "Personal Visa".</summary>
    public string? Label { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

#pragma warning disable CS8618 // Required by Entity Framework
    private SavedCard() { }
#pragma warning restore CS8618

    public SavedCard(string buyerId, string vaultTokenId, string? payPalCustomerId,
        string brand, string last4, string expiry, string? label)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(vaultTokenId, nameof(vaultTokenId));
        Guard.Against.NullOrEmpty(brand, nameof(brand));
        Guard.Against.NullOrEmpty(last4, nameof(last4));
        Guard.Against.NullOrEmpty(expiry, nameof(expiry));

        BuyerId = buyerId;
        VaultTokenId = vaultTokenId;
        PayPalCustomerId = payPalCustomerId;
        Brand = brand;
        Last4 = last4;
        Expiry = expiry;
        Label = label;
    }
}
