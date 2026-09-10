using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;

/// <summary>
/// A card a shopper has saved (vaulted at PayPal) for reuse on later orders. This app never stores
/// the card number: only PayPal's vault token id plus safe display details (brand, last four,
/// expiry) so the shopper can recognise which card it is. Owned by the shopper who saved it,
/// identified by <see cref="BuyerId"/> (the same string identity used by orders/baskets).
/// </summary>
public class SavedCard : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SavedCard() { }

    public SavedCard(string buyerId, string payPalVaultId, string brand, string last4, string expiry, string? cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalVaultId, nameof(payPalVaultId));

        BuyerId = buyerId;
        PayPalVaultId = payPalVaultId;
        Brand = brand;
        Last4 = last4;
        Expiry = expiry;
        CardholderName = cardholderName;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The owning shopper (email/username identity), same as Order.BuyerId.</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal Vault v3 payment-token id used to charge this card later.</summary>
    public string PayPalVaultId { get; private set; }

    public string Brand { get; private set; }
    public string Last4 { get; private set; }

    /// <summary>Card expiry as reported by PayPal (YYYY-MM).</summary>
    public string Expiry { get; private set; }

    public string? CardholderName { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
