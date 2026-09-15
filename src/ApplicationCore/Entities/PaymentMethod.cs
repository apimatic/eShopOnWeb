using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class PaymentMethod : BaseEntity, IAggregateRoot
{
    private PaymentMethod() { }

    public PaymentMethod(string buyerId, string payPalPaymentTokenId, string payPalCustomerId,
        string brand, string lastDigits, string expiry, DateTimeOffset createdAt)
    {
        BuyerId = Guard.Against.NullOrEmpty(buyerId);
        PayPalPaymentTokenId = Guard.Against.NullOrEmpty(payPalPaymentTokenId);
        PayPalCustomerId = Guard.Against.NullOrEmpty(payPalCustomerId);
        Brand = Guard.Against.NullOrEmpty(brand);
        LastDigits = Guard.Against.NullOrEmpty(lastDigits);
        Expiry = Guard.Against.NullOrEmpty(expiry);
        CreatedAt = createdAt;
    }

    public string BuyerId { get; private set; } = null!;
    public string PayPalPaymentTokenId { get; private set; } = null!;
    public string PayPalCustomerId { get; private set; } = null!;
    public string Brand { get; private set; } = null!;
    public string LastDigits { get; private set; } = null!;
    public string Expiry { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public bool IsDeleted => DeletedAt.HasValue;

    public void Delete(DateTimeOffset deletedAt) => DeletedAt = deletedAt;
}
