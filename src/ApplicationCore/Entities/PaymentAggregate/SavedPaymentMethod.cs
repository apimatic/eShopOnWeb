using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A card the shopper has vaulted with PayPal. Only safe display data (brand, last
/// digits, expiry) is stored here; the full card details live exclusively in PayPal's
/// vault and are referenced by the PayPal payment token id.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() {}

    public SavedPaymentMethod(string buyerId, string payPalCustomerId, string payPalPaymentTokenId,
        string? brand, string? lastFourDigits, string? expiry, string? cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalPaymentTokenId, nameof(payPalPaymentTokenId));

        BuyerId = buyerId;
        PayPalCustomerId = payPalCustomerId;
        PayPalPaymentTokenId = payPalPaymentTokenId;
        Brand = brand;
        LastFourDigits = lastFourDigits;
        Expiry = expiry;
        CardholderName = cardholderName;
    }

    public string BuyerId { get; private set; }
    public string PayPalCustomerId { get; private set; }
    public string PayPalPaymentTokenId { get; private set; }
    public string? Brand { get; private set; }
    public string? LastFourDigits { get; private set; }
    public string? Expiry { get; private set; }
    public string? CardholderName { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
}
