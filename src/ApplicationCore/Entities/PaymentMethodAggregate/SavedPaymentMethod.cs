using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

/// <summary>
/// Shopper-owned reference to a PayPal vault payment token. Stores no PAN or CVC.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618
    private SavedPaymentMethod() { }
#pragma warning restore CS8618

    public SavedPaymentMethod(
        string buyerId,
        string paymentTokenId,
        string? payPalCustomerId,
        string? lastDigits,
        string? brand,
        string? expiry,
        string? cardholderName,
        string? cardType)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(paymentTokenId, nameof(paymentTokenId));

        BuyerId = buyerId;
        PaymentTokenId = paymentTokenId;
        PayPalCustomerId = payPalCustomerId;
        LastDigits = lastDigits;
        Brand = brand;
        Expiry = expiry;
        CardholderName = cardholderName;
        CardType = cardType;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string BuyerId { get; private set; }
    public string PaymentTokenId { get; private set; }
    public string? PayPalCustomerId { get; private set; }
    public string? LastDigits { get; private set; }
    public string? Brand { get; private set; }
    public string? Expiry { get; private set; }
    public string? CardholderName { get; private set; }
    public string? CardType { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
