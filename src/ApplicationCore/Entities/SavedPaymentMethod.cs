using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    private SavedPaymentMethod() { }

    public SavedPaymentMethod(string buyerId, string payPalPaymentTokenId, string? payPalCustomerId,
        string brand, string lastDigits, string expiry, string? cardholderName)
    {
        BuyerId = buyerId;
        PayPalPaymentTokenId = payPalPaymentTokenId;
        PayPalCustomerId = payPalCustomerId;
        Brand = brand;
        LastDigits = lastDigits;
        Expiry = expiry;
        CardholderName = cardholderName;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string BuyerId { get; private set; } = string.Empty;
    public string PayPalPaymentTokenId { get; private set; } = string.Empty;
    public string? PayPalCustomerId { get; private set; }
    public string Brand { get; private set; } = string.Empty;
    public string LastDigits { get; private set; } = string.Empty;
    public string Expiry { get; private set; } = string.Empty;
    public string? CardholderName { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public bool IsDeleted => DeletedAt != null;

    public void Delete() => DeletedAt ??= DateTimeOffset.UtcNow;
}
