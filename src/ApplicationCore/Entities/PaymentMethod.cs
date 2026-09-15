using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class PaymentMethod : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() { }

    public PaymentMethod(string buyerId, string paypalPaymentTokenId, string brand, string lastDigits,
        string expiry)
    {
        BuyerId = buyerId;
        PayPalPaymentTokenId = paypalPaymentTokenId;
        Brand = brand;
        LastDigits = lastDigits;
        Expiry = expiry;
    }

    public string BuyerId { get; private set; }
    public string PayPalPaymentTokenId { get; private set; }
    public string Brand { get; private set; }
    public string LastDigits { get; private set; }
    public string Expiry { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public void Deactivate() => IsActive = false;
}
