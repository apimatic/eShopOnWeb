using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class PaymentMethod : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618
    private PaymentMethod() { }

    public PaymentMethod(string buyerId, string vaultCustomerId, string payPalTokenId, string? last4, string? brand, string? expiry)
    {
        BuyerId = buyerId;
        VaultCustomerId = vaultCustomerId;
        PayPalTokenId = payPalTokenId;
        Last4 = last4;
        Brand = brand;
        Expiry = expiry;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string BuyerId { get; private set; }
    public string VaultCustomerId { get; private set; }
    public string PayPalTokenId { get; private set; }
    public string? Last4 { get; private set; }
    public string? Brand { get; private set; }
    public string? Expiry { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
