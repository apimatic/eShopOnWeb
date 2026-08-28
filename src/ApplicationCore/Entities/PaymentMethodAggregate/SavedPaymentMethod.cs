using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    private SavedPaymentMethod() { }

    public SavedPaymentMethod(string ownerId, string paypalCustomerId, string paypalPaymentTokenId,
        string brand, string last4, string expiry)
    {
        OwnerId = ownerId;
        PayPalCustomerId = paypalCustomerId;
        PayPalPaymentTokenId = paypalPaymentTokenId;
        Brand = brand;
        Last4 = last4;
        Expiry = expiry;
    }

    public string OwnerId { get; private set; } = string.Empty;
    public string PayPalCustomerId { get; private set; } = string.Empty;
    public string PayPalPaymentTokenId { get; private set; } = string.Empty;
    public string Brand { get; private set; } = string.Empty;
    public string Last4 { get; private set; } = string.Empty;
    public string Expiry { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
}
