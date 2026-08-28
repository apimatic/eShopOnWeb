using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

/// <summary>A safe local reference to a card held in PayPal's vault. No PAN or security code is stored.</summary>
public class PaymentMethod : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() { }

    public PaymentMethod(string buyerId, string payPalPaymentTokenId, string? payPalCustomerId,
        string brand, string last4, string expiry)
    {
        BuyerId = Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        PayPalPaymentTokenId = Guard.Against.NullOrEmpty(payPalPaymentTokenId, nameof(payPalPaymentTokenId));
        PayPalCustomerId = payPalCustomerId;
        Brand = Guard.Against.NullOrEmpty(brand, nameof(brand));
        Last4 = Guard.Against.NullOrEmpty(last4, nameof(last4));
        Expiry = Guard.Against.NullOrEmpty(expiry, nameof(expiry));
    }

    public string BuyerId { get; private set; }
    public string PayPalPaymentTokenId { get; private set; }
    public string? PayPalCustomerId { get; private set; }
    public string Brand { get; private set; }
    public string Last4 { get; private set; }
    public string Expiry { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
}
