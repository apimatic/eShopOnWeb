using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

public class PaymentMethod : BaseEntity, IAggregateRoot
{
    private PaymentMethod() { }

    public PaymentMethod(string ownerId, string payPalCustomerId, string payPalPaymentTokenId,
        string? name, string? brand, string? lastDigits, string? expiry, string? type)
    {
        OwnerId = ownerId;
        PayPalCustomerId = payPalCustomerId;
        PayPalPaymentTokenId = payPalPaymentTokenId;
        Name = name;
        Brand = brand;
        LastDigits = lastDigits;
        Expiry = expiry;
        Type = type;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string OwnerId { get; private set; } = string.Empty;
    public string PayPalCustomerId { get; private set; } = string.Empty;
    public string PayPalPaymentTokenId { get; private set; } = string.Empty;
    public string? Name { get; private set; }
    public string? Brand { get; private set; }
    public string? LastDigits { get; private set; }
    public string? Expiry { get; private set; }
    public string? Type { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public bool IsActive => DeletedAt == null;

    public void Delete() => DeletedAt = DateTimeOffset.UtcNow;
}
