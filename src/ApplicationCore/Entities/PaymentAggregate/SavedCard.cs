using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A card a shopper has saved for reuse (Flow 2). The application never stores full card details: only the
/// PayPal vault token needed to charge the card again, plus safe descriptors so the shopper can recognise
/// which card it is. A saved card belongs to the shopper who saved it.
/// </summary>
public class SavedCard : BaseEntity, IAggregateRoot
{
    /// <summary>The identity of the shopper who owns this saved card.</summary>
    public string BuyerId { get; private set; }

    /// <summary>The PayPal vault token id used to charge this card. This is not card data.</summary>
    public string VaultId { get; private set; }

    /// <summary>The PayPal customer id the card is vaulted under.</summary>
    public string PayPalCustomerId { get; private set; }

    /// <summary>The card network/brand, e.g. VISA. Safe to show.</summary>
    public string? Brand { get; private set; }

    /// <summary>The last four digits, so the shopper can recognise the card. Not full card data.</summary>
    public string? Last4 { get; private set; }

    /// <summary>The expiry as YYYY-MM. Safe to show.</summary>
    public string? Expiry { get; private set; }

    /// <summary>The cardholder name as it appears on the card.</summary>
    public string? CardholderName { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

#pragma warning disable CS8618 // Required by Entity Framework
    private SavedCard() { }
#pragma warning restore CS8618

    public SavedCard(string buyerId, string vaultId, string payPalCustomerId,
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

    /// <summary>A safe, human-readable description, e.g. "VISA ****1111".</summary>
    public string Description => $"{Brand ?? "CARD"} ****{Last4 ?? "----"}";
}
