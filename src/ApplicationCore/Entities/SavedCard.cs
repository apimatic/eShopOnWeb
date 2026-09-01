using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

/// <summary>
/// A card the shopper vaulted with PayPal for reuse. Only safe descriptors are stored —
/// never the card number or security code. <see cref="PayPalPaymentTokenId"/> is the vault
/// token used to pay; <see cref="PayPalCustomerId"/> scopes the shopper's vault at PayPal.
/// </summary>
public class SavedCard : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SavedCard() { }

    public SavedCard(string buyerId, string payPalCustomerId, string payPalPaymentTokenId,
        string? brand, string? lastDigits, string? expiry)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalCustomerId, nameof(payPalCustomerId));
        Guard.Against.NullOrEmpty(payPalPaymentTokenId, nameof(payPalPaymentTokenId));

        BuyerId = buyerId;
        PayPalCustomerId = payPalCustomerId;
        PayPalPaymentTokenId = payPalPaymentTokenId;
        Brand = brand;
        LastDigits = lastDigits;
        Expiry = expiry;
    }

    /// <summary>Identity username of the shopper who owns this card.</summary>
    public string BuyerId { get; private set; }

    public string PayPalCustomerId { get; private set; }
    public string PayPalPaymentTokenId { get; private set; }

    /// <summary>Safe display descriptors only (e.g. "VISA", "1111", "2030-01").</summary>
    public string? Brand { get; private set; }
    public string? LastDigits { get; private set; }
    public string? Expiry { get; private set; }

    public DateTimeOffset CreatedOn { get; private set; } = DateTimeOffset.UtcNow;
}
