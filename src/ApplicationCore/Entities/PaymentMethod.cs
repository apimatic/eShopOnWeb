using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class PaymentMethod : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618
    private PaymentMethod() { }

    public PaymentMethod(string buyerId, string payPalVaultId, string? lastFour, string? brand, string? expiry, string? cardholderName)
    {
        BuyerId = buyerId;
        PayPalVaultId = payPalVaultId;
        LastFour = lastFour;
        Brand = brand;
        Expiry = expiry;
        CardholderName = cardholderName;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string BuyerId { get; private set; }
    public string PayPalVaultId { get; private set; }
    public string? LastFour { get; private set; }
    public string? Brand { get; private set; }
    public string? Expiry { get; private set; }
    public string? CardholderName { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
