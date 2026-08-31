using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    private SavedPaymentMethod() { }

    public SavedPaymentMethod(string buyerId, string payPalTokenId, string brand, string lastDigits, string expiry)
    {
        BuyerId = buyerId;
        PayPalTokenId = payPalTokenId;
        Brand = brand;
        LastDigits = lastDigits;
        Expiry = expiry;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string BuyerId { get; private set; } = null!;
    public string PayPalTokenId { get; private set; } = null!;
    public string Brand { get; private set; } = null!;
    public string LastDigits { get; private set; } = null!;
    public string Expiry { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
}
