using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618
    private SavedPaymentMethod() { }
#pragma warning restore CS8618

    public SavedPaymentMethod(
        string buyerIdentityGuid,
        string payPalVaultId,
        string payPalCustomerId,
        string last4,
        string brand,
        int expiryYear,
        int expiryMonth)
    {
        BuyerIdentityGuid = buyerIdentityGuid;
        PayPalVaultId = payPalVaultId;
        PayPalCustomerId = payPalCustomerId;
        Last4 = last4;
        Brand = brand;
        ExpiryYear = expiryYear;
        ExpiryMonth = expiryMonth;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string BuyerIdentityGuid { get; private set; }
    public string PayPalVaultId { get; private set; }
    public string PayPalCustomerId { get; private set; }
    public string Last4 { get; private set; }
    public string Brand { get; private set; }
    public int ExpiryYear { get; private set; }
    public int ExpiryMonth { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
