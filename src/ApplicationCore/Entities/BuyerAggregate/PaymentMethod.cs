using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

public class PaymentMethod : BaseEntity, IAggregateRoot
{
    private PaymentMethod() { }

    public PaymentMethod(string buyerId, string payPalPaymentTokenId, string? payPalCustomerId,
        string brand, string last4, string expiry)
    {
        BuyerId = Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        PayPalPaymentTokenId = Guard.Against.NullOrEmpty(payPalPaymentTokenId, nameof(payPalPaymentTokenId));
        PayPalCustomerId = payPalCustomerId;
        Brand = Guard.Against.NullOrEmpty(brand, nameof(brand));
        Last4 = Guard.Against.NullOrEmpty(last4, nameof(last4));
        Expiry = Guard.Against.NullOrEmpty(expiry, nameof(expiry));
        CreatedAt = DateTimeOffset.UtcNow;
        IsActive = true;
    }

    public string BuyerId { get; private set; } = string.Empty;
    public string PayPalPaymentTokenId { get; private set; } = string.Empty;
    public string? PayPalCustomerId { get; private set; }
    public string Brand { get; private set; } = string.Empty;
    public string Last4 { get; private set; } = string.Empty;
    public string Expiry { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    public void Deactivate()
    {
        IsActive = false;
        DeletedAt = DateTimeOffset.UtcNow;
    }
}
