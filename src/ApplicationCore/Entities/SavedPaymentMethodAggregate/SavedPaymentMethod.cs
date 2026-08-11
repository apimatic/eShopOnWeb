using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SavedPaymentMethodAggregate;

/// <summary>
/// A card a shopper saved for reuse. The application's own database never holds full card
/// details — only PayPal's vault token id plus a safe descriptor (brand, last four, expiry)
/// the shopper can recognise. A saved card belongs to the shopper who saved it.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    /// <summary>Owner of the saved card (the shopper's identity).</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal vault payment-token id. Used to charge the card later; not card data.</summary>
    public string VaultId { get; private set; }

    public string Brand { get; private set; }
    public string Last4 { get; private set; }

    /// <summary>Expiry in PayPal's YYYY-MM form, safe to show.</summary>
    public string Expiry { get; private set; }

    public string? CardholderName { get; private set; }
    public string? CardType { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

#pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }
#pragma warning restore CS8618

    public SavedPaymentMethod(string buyerId, string vaultId, string brand, string last4, string expiry,
        string? cardholderName, string? cardType)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(vaultId, nameof(vaultId));

        BuyerId = buyerId;
        VaultId = vaultId;
        Brand = brand;
        Last4 = last4;
        Expiry = expiry;
        CardholderName = cardholderName;
        CardType = cardType;
    }
}
