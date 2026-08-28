using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    private SavedPaymentMethod() { }

    public SavedPaymentMethod(string ownerId, string payPalTokenId, string payPalCustomerId,
        string merchantCustomerId, string? brand, string? lastDigits, string? expiry, string? name)
    {
        OwnerId = ownerId;
        PayPalTokenId = payPalTokenId;
        PayPalCustomerId = payPalCustomerId;
        MerchantCustomerId = merchantCustomerId;
        Brand = brand;
        LastDigits = lastDigits;
        Expiry = expiry;
        CardholderName = name;
        IsActive = true;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string OwnerId { get; private set; } = string.Empty;
    public string PayPalTokenId { get; private set; } = string.Empty;
    public string PayPalCustomerId { get; private set; } = string.Empty;
    public string MerchantCustomerId { get; private set; } = string.Empty;
    public string? Brand { get; private set; }
    public string? LastDigits { get; private set; }
    public string? Expiry { get; private set; }
    public string? CardholderName { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    public void Deactivate()
    {
        IsActive = false;
        DeletedAt = DateTimeOffset.UtcNow;
    }
}
