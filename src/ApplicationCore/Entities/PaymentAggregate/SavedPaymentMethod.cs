using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618
    private SavedPaymentMethod() { }

    public SavedPaymentMethod(string buyerId, string paypalPaymentTokenId, string? paypalCustomerId,
        string brand, string last4, string expiry)
    {
        BuyerId = Guard.Against.NullOrEmpty(buyerId);
        PayPalPaymentTokenId = Guard.Against.NullOrEmpty(paypalPaymentTokenId);
        PayPalCustomerId = paypalCustomerId;
        Brand = Guard.Against.NullOrEmpty(brand);
        Last4 = Guard.Against.NullOrEmpty(last4);
        Expiry = Guard.Against.NullOrEmpty(expiry);
    }

    public string BuyerId { get; private set; }
    public string PayPalPaymentTokenId { get; private set; }
    public string? PayPalCustomerId { get; private set; }
    public string Brand { get; private set; }
    public string Last4 { get; private set; }
    public string Expiry { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
}
