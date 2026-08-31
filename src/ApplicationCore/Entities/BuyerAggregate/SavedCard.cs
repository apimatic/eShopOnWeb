using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

/// <summary>
/// A card the shopper vaulted with PayPal for reuse. Only safe, recognisable details
/// (brand, last four digits, expiry) are kept here; the full card data lives only
/// in PayPal's vault, referenced by the payment token id.
/// </summary>
public class SavedCard : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SavedCard() { }

    public SavedCard(string buyerId, string vaultPaymentTokenId, string? payPalCustomerId,
        string brand, string last4, string? expiry, string? cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(vaultPaymentTokenId, nameof(vaultPaymentTokenId));
        Guard.Against.NullOrEmpty(last4, nameof(last4));

        BuyerId = buyerId;
        VaultPaymentTokenId = vaultPaymentTokenId;
        PayPalCustomerId = payPalCustomerId;
        Brand = brand;
        Last4 = last4;
        Expiry = expiry;
        CardholderName = cardholderName;
    }

    public string BuyerId { get; private set; }
    public string VaultPaymentTokenId { get; private set; }
    public string? PayPalCustomerId { get; private set; }
    public string Brand { get; private set; }
    public string Last4 { get; private set; }
    public string? Expiry { get; private set; }
    public string? CardholderName { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
}
