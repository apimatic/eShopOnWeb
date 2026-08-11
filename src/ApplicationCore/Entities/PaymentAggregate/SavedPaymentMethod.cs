using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A card a shopper has vaulted with PayPal for reuse. The application database keeps only a
/// safe, non-sensitive descriptor (brand, last four, expiry) plus the PayPal vault token that
/// stands in for the card. A full card number is never stored here.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }

    public SavedPaymentMethod(string buyerId, string vaultId, string? brand, string last4,
        string? expiry, string? cardholderName, string? label)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(vaultId, nameof(vaultId));
        Guard.Against.NullOrEmpty(last4, nameof(last4));

        BuyerId = buyerId;
        VaultId = vaultId;
        Brand = brand;
        Last4 = last4;
        Expiry = expiry;
        CardholderName = cardholderName;
        Label = label;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Owner of the saved card, for ownership scoping.</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal vault payment-token id that represents the stored card.</summary>
    public string VaultId { get; private set; }

    public string? Brand { get; private set; }

    /// <summary>Last four digits, safe to show the shopper.</summary>
    public string Last4 { get; private set; }

    /// <summary>Expiry (YYYY-MM), safe to show the shopper.</summary>
    public string? Expiry { get; private set; }

    public string? CardholderName { get; private set; }

    /// <summary>Optional shopper-friendly label.</summary>
    public string? Label { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
