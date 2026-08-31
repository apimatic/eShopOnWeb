using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    private SavedPaymentMethod() { }

    public SavedPaymentMethod(string buyerId, string payPalPaymentTokenId, string? payPalCustomerId,
        string brand, string last4, string expiry, DateTimeOffset createdAt)
    {
        BuyerId = Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        PayPalPaymentTokenId = Guard.Against.NullOrEmpty(payPalPaymentTokenId, nameof(payPalPaymentTokenId));
        PayPalCustomerId = payPalCustomerId;
        Brand = Guard.Against.NullOrEmpty(brand, nameof(brand));
        Last4 = Guard.Against.NullOrEmpty(last4, nameof(last4));
        Expiry = Guard.Against.NullOrEmpty(expiry, nameof(expiry));
        CreatedAt = createdAt;
    }

    public string BuyerId { get; private set; } = string.Empty;
    public string PayPalPaymentTokenId { get; private set; } = string.Empty;
    public string? PayPalCustomerId { get; private set; }
    public string Brand { get; private set; } = string.Empty;
    public string Last4 { get; private set; } = string.Empty;
    public string Expiry { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public bool IsActive => DeletedAt is null;

    public void MarkDeleted(DateTimeOffset deletedAt) => DeletedAt ??= deletedAt;
}
