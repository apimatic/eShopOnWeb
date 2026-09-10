using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

/// <summary>
/// A card a shopper has saved for reuse. The card itself lives in PayPal's vault; this app keeps only
/// the vault token and safe-to-display metadata (brand, last four, expiry) — never full card details.
/// Owner-scoped by <see cref="BuyerId"/>: one shopper never sees, uses, or deletes another's.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    public string BuyerId { get; private set; }

    /// <summary>The PayPal-generated vault token id used to charge this card later.</summary>
    public string VaultId { get; private set; }

    /// <summary>The PayPal customer id this shopper's saved cards are grouped under.</summary>
    public string PayPalCustomerId { get; private set; }

    public string? Brand { get; private set; }
    public string? Last4 { get; private set; }
    public string? Expiry { get; private set; }
    public string? CardholderName { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

#pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }
#pragma warning restore CS8618

    public SavedPaymentMethod(
        string buyerId, string vaultId, string payPalCustomerId,
        string? brand, string? last4, string? expiry, string? cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(vaultId, nameof(vaultId));
        Guard.Against.NullOrEmpty(payPalCustomerId, nameof(payPalCustomerId));

        BuyerId = buyerId;
        VaultId = vaultId;
        PayPalCustomerId = payPalCustomerId;
        Brand = brand;
        Last4 = last4;
        Expiry = expiry;
        CardholderName = cardholderName;
    }
}
