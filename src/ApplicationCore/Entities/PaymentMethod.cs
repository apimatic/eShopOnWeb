using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class PaymentMethod : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618
    private PaymentMethod() { }
#pragma warning restore CS8618

    public PaymentMethod(string buyerId, string payPalTokenId, string payPalCustomerId,
        string brand, string lastDigits, string expiry)
    {
        BuyerId = buyerId;
        PayPalTokenId = payPalTokenId;
        PayPalCustomerId = payPalCustomerId;
        Brand = brand;
        LastDigits = lastDigits;
        Expiry = expiry;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string BuyerId { get; private set; }
    public string PayPalTokenId { get; private set; }
    public string PayPalCustomerId { get; private set; }
    public string Brand { get; private set; }
    public string LastDigits { get; private set; }
    public string Expiry { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public bool IsActive => DeletedAt is null;

    public void Delete() => DeletedAt = DateTimeOffset.UtcNow;
}
