using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }
#pragma warning restore CS8618

    public SavedPaymentMethod(
        string buyerId,
        string payPalPaymentTokenId,
        string? payPalCustomerId,
        string brand,
        string lastDigits,
        string expiry)
    {
        BuyerId = Guard.Against.NullOrWhiteSpace(buyerId, nameof(buyerId));
        PayPalPaymentTokenId = Guard.Against.NullOrWhiteSpace(payPalPaymentTokenId, nameof(payPalPaymentTokenId));
        PayPalCustomerId = payPalCustomerId;
        Brand = Guard.Against.NullOrWhiteSpace(brand, nameof(brand));
        LastDigits = Guard.Against.NullOrWhiteSpace(lastDigits, nameof(lastDigits));
        Expiry = Guard.Against.NullOrWhiteSpace(expiry, nameof(expiry));
    }

    public string BuyerId { get; private set; }
    public string PayPalPaymentTokenId { get; private set; }
    public string? PayPalCustomerId { get; private set; }
    public string Brand { get; private set; }
    public string LastDigits { get; private set; }
    public string Expiry { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RemovedAt { get; private set; }

    public void Remove(DateTimeOffset removedAt)
    {
        IsActive = false;
        RemovedAt = removedAt;
    }
}
