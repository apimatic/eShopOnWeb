using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class PaymentMethod : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() { }
#pragma warning restore CS8618

    public PaymentMethod(string buyerId, string paypalPaymentTokenId, string brand,
        string lastDigits, string expiry, DateTimeOffset createdAt)
    {
        BuyerId = Guard.Against.NullOrEmpty(buyerId);
        PaypalPaymentTokenId = Guard.Against.NullOrEmpty(paypalPaymentTokenId);
        Brand = Guard.Against.NullOrEmpty(brand);
        LastDigits = Guard.Against.NullOrEmpty(lastDigits);
        Expiry = Guard.Against.NullOrEmpty(expiry);
        CreatedAt = createdAt;
    }

    public string BuyerId { get; private set; }
    public string PaypalPaymentTokenId { get; private set; }
    public string Brand { get; private set; }
    public string LastDigits { get; private set; }
    public string Expiry { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public bool IsDeleted => DeletedAt.HasValue;

    public void Delete(DateTimeOffset deletedAt)
    {
        DeletedAt ??= deletedAt;
    }
}
