using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

public class PaymentMethod : BaseEntity, IAggregateRoot
{
    private PaymentMethod() { }

    public PaymentMethod(string buyerId, string payPalCustomerId, string payPalTokenId,
        string? cardholderName, string? brand, string? lastDigits, string? expiry)
    {
        BuyerId = buyerId;
        PayPalCustomerId = payPalCustomerId;
        PayPalTokenId = payPalTokenId;
        CardholderName = cardholderName;
        Brand = brand;
        LastDigits = lastDigits;
        Expiry = expiry;
        IsActive = true;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string BuyerId { get; private set; } = string.Empty;
    public string PayPalCustomerId { get; private set; } = string.Empty;
    public string PayPalTokenId { get; private set; } = string.Empty;
    public string? CardholderName { get; private set; }
    public string? Brand { get; private set; }
    public string? LastDigits { get; private set; }
    public string? Expiry { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    public void Delete()
    {
        IsActive = false;
        DeletedAt = DateTimeOffset.UtcNow;
    }
}
