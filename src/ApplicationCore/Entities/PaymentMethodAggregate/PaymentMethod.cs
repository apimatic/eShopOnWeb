using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

public class PaymentMethod : IAggregateRoot
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string BuyerId { get; private set; }
    public string PayPalPaymentTokenId { get; private set; }
    public string Brand { get; private set; }
    public string Last4 { get; private set; }
    public string Expiry { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.Now;

    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() { }

    public PaymentMethod(string buyerId, string payPalPaymentTokenId, string brand, string last4, string expiry)
    {
        BuyerId = buyerId;
        PayPalPaymentTokenId = payPalPaymentTokenId;
        Brand = brand ?? string.Empty;
        Last4 = last4 ?? string.Empty;
        Expiry = expiry ?? string.Empty;
    }
}