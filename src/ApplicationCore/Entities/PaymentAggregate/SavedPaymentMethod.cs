using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by EF Core
    private SavedPaymentMethod() { }
#pragma warning restore CS8618

    public SavedPaymentMethod(string buyerId, string paypalPaymentTokenId, string paypalCustomerId,
        string brand, string last4, string expiry, DateTimeOffset now)
    {
        BuyerId = buyerId;
        PayPalPaymentTokenId = paypalPaymentTokenId;
        PayPalCustomerId = paypalCustomerId;
        Brand = brand;
        Last4 = last4;
        Expiry = expiry;
        CreatedAt = now;
    }

    public string BuyerId { get; private set; }
    public string PayPalPaymentTokenId { get; private set; }
    public string PayPalCustomerId { get; private set; }
    public string Brand { get; private set; }
    public string Last4 { get; private set; }
    public string Expiry { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public bool IsActive => DeletedAt is null;

    public void Deactivate(DateTimeOffset now) => DeletedAt ??= now;
}
