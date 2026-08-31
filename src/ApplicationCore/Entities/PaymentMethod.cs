using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class PaymentMethod : BaseEntity, IAggregateRoot
{
    private PaymentMethod() { }

    public PaymentMethod(
        string buyerId,
        string payPalPaymentTokenId,
        string payPalCustomerId,
        string? brand,
        string? cardType,
        string lastDigits,
        string? expiry)
    {
        BuyerId = buyerId;
        PayPalPaymentTokenId = payPalPaymentTokenId;
        PayPalCustomerId = payPalCustomerId;
        Brand = brand;
        CardType = cardType;
        LastDigits = lastDigits;
        Expiry = expiry;
        IsActive = true;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string BuyerId { get; private set; } = string.Empty;
    public string PayPalPaymentTokenId { get; private set; } = string.Empty;
    public string PayPalCustomerId { get; private set; } = string.Empty;
    public string? Brand { get; private set; }
    public string? CardType { get; private set; }
    public string LastDigits { get; private set; } = string.Empty;
    public string? Expiry { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    public void Deactivate()
    {
        IsActive = false;
        DeletedAt = DateTimeOffset.UtcNow;
    }
}
