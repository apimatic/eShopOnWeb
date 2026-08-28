using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618
    private SavedPaymentMethod() { }
#pragma warning restore CS8618

    public SavedPaymentMethod(string buyerId, string paypalTokenId, string? paypalCustomerId,
        string brand, string lastDigits, string expiry)
    {
        BuyerId = buyerId;
        PayPalTokenId = paypalTokenId;
        PayPalCustomerId = paypalCustomerId;
        Brand = brand;
        LastDigits = lastDigits;
        Expiry = expiry;
    }

    public string BuyerId { get; private set; }
    public string PayPalTokenId { get; private set; }
    public string? PayPalCustomerId { get; private set; }
    public string Brand { get; private set; }
    public string LastDigits { get; private set; }
    public string Expiry { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeletedAt { get; private set; }
    public bool IsDeleted => DeletedAt.HasValue;

    public void Delete() => DeletedAt ??= DateTimeOffset.UtcNow;
}
