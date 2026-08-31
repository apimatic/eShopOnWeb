using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

/// <summary>
/// A card the shopper vaulted with PayPal for later use. Holds only the PayPal
/// vault identifiers and safe display data (brand, last four digits, expiry);
/// full card details are never stored.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }

    public SavedPaymentMethod(string ownerId, string payPalCustomerId, string payPalPaymentTokenId,
        string cardBrand, string last4, string? expiry, string? cardholderName)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(payPalCustomerId, nameof(payPalCustomerId));
        Guard.Against.NullOrEmpty(payPalPaymentTokenId, nameof(payPalPaymentTokenId));
        Guard.Against.NullOrEmpty(cardBrand, nameof(cardBrand));
        Guard.Against.NullOrEmpty(last4, nameof(last4));

        OwnerId = ownerId;
        PayPalCustomerId = payPalCustomerId;
        PayPalPaymentTokenId = payPalPaymentTokenId;
        CardBrand = cardBrand;
        Last4 = last4;
        Expiry = expiry;
        CardholderName = cardholderName;
    }

    /// <summary>The shopper who saved the card (identity username).</summary>
    public string OwnerId { get; private set; }
    public string PayPalCustomerId { get; private set; }
    public string PayPalPaymentTokenId { get; private set; }
    public string CardBrand { get; private set; }
    public string Last4 { get; private set; }
    /// <summary>Card expiry in PayPal's YYYY-MM format.</summary>
    public string? Expiry { get; private set; }
    public string? CardholderName { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public string Describe() => $"{CardBrand} x-{Last4}";
}
