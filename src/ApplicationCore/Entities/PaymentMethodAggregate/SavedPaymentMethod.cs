using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618
    private SavedPaymentMethod() { }
#pragma warning restore CS8618

    public SavedPaymentMethod(
        string buyerId,
        string payPalPaymentTokenId,
        string? payPalCustomerId,
        string? brand,
        string? lastDigits,
        string? expiry,
        string? cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalPaymentTokenId, nameof(payPalPaymentTokenId));

        BuyerId = buyerId;
        PayPalPaymentTokenId = payPalPaymentTokenId;
        PayPalCustomerId = payPalCustomerId;
        Brand = brand;
        LastDigits = lastDigits;
        Expiry = expiry;
        CardholderName = cardholderName;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string BuyerId { get; private set; }
    public string PayPalPaymentTokenId { get; private set; }
    public string? PayPalCustomerId { get; private set; }
    public string? Brand { get; private set; }
    public string? LastDigits { get; private set; }
    public string? Expiry { get; private set; }
    public string? CardholderName { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
