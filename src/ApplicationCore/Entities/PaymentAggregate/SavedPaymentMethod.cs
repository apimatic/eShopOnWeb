using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    private SavedPaymentMethod() { }

    public SavedPaymentMethod(string buyerId, string payPalTokenId, string? payPalCustomerId,
        string brand, string lastFour, string expiry)
    {
        BuyerId = buyerId;
        PayPalTokenId = payPalTokenId;
        PayPalCustomerId = payPalCustomerId;
        Brand = brand;
        LastFour = lastFour;
        Expiry = expiry;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string BuyerId { get; private set; } = null!;
    public string PayPalTokenId { get; private set; } = null!;
    public string? PayPalCustomerId { get; private set; }
    public string Brand { get; private set; } = null!;
    public string LastFour { get; private set; } = null!;
    public string Expiry { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public bool IsDeleted { get; private set; }

    public void Delete() => IsDeleted = true;
}
