using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

public class PaymentMethod : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() { }

    public PaymentMethod(string ownerId, string paypalVaultId, string brand, string last4,
        string expiry, DateTimeOffset createdAt)
    {
        OwnerId = ownerId;
        PayPalVaultId = paypalVaultId;
        Brand = brand;
        Last4 = last4;
        Expiry = expiry;
        CreatedAt = createdAt;
    }

    public string OwnerId { get; private set; }
    public string PayPalVaultId { get; private set; }
    public string Brand { get; private set; }
    public string Last4 { get; private set; }
    public string Expiry { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
