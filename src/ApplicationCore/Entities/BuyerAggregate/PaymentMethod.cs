using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

public class PaymentMethod : BaseEntity, IAggregateRoot
{
    private PaymentMethod() { }

    public PaymentMethod(string buyerId, string payPalVaultId, string? payPalCustomerId,
        string brand, string lastDigits, string? expiry)
    {
        BuyerId = buyerId;
        PayPalVaultId = payPalVaultId;
        PayPalCustomerId = payPalCustomerId;
        Brand = brand;
        LastDigits = lastDigits;
        Expiry = expiry;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string BuyerId { get; private set; } = string.Empty;
    public string PayPalVaultId { get; private set; } = string.Empty;
    public string? PayPalCustomerId { get; private set; }
    public string Brand { get; private set; } = string.Empty;
    public string LastDigits { get; private set; } = string.Empty;
    public string? Expiry { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public bool IsDeleted { get; private set; }

    public void MarkDeleted() => IsDeleted = true;
}
