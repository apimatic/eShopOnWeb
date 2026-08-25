using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

public class PaymentMethod : BaseEntity
{
    public int BuyerId { get; private set; }
    public string? Alias { get; private set; }

    // The PayPal vault payment-token id. Actual card data is never stored here or anywhere in this app.
    public string PayPalVaultId { get; private set; }
    public string? Brand { get; private set; }
    public string? Last4 { get; private set; }
    public string? Expiry { get; private set; }
    public string? CardType { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() { }

    public PaymentMethod(int buyerId, string payPalVaultId, string? alias, string? brand, string? last4, string? expiry, string? cardType)
    {
        BuyerId = buyerId;
        PayPalVaultId = payPalVaultId;
        Alias = alias;
        Brand = brand;
        Last4 = last4;
        Expiry = expiry;
        CardType = cardType;
        CreatedAt = DateTimeOffset.UtcNow;
    }
}
