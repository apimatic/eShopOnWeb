using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    private SavedPaymentMethod() { }

    public SavedPaymentMethod(
        string buyerId,
        string payPalTokenId,
        string payPalCustomerId,
        string brand,
        string lastDigits,
        string expiry,
        string? cardholderName)
    {
        BuyerId = buyerId;
        PayPalTokenId = payPalTokenId;
        PayPalCustomerId = payPalCustomerId;
        Brand = brand;
        LastDigits = lastDigits;
        Expiry = expiry;
        CardholderName = cardholderName;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string BuyerId { get; private set; } = null!;
    public string PayPalTokenId { get; private set; } = null!;
    public string PayPalCustomerId { get; private set; } = null!;
    public string Brand { get; private set; } = null!;
    public string LastDigits { get; private set; } = null!;
    public string Expiry { get; private set; } = null!;
    public string? CardholderName { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public bool IsActive => DeletedAt == null;

    public void Delete() => DeletedAt ??= DateTimeOffset.UtcNow;
}
