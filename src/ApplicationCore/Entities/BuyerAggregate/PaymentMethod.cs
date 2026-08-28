using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

/// <summary>
/// A safe local reference to a payment instrument held in PayPal's vault.
/// The PAN and security code are deliberately never represented by this entity.
/// </summary>
public class PaymentMethod : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() { }

    public PaymentMethod(string buyerId, string vaultId, string? paypalCustomerId,
        string brand, string last4, string expiry)
    {
        BuyerId = buyerId;
        VaultId = vaultId;
        PayPalCustomerId = paypalCustomerId;
        Brand = brand;
        Last4 = last4;
        Expiry = expiry;
    }

    public string BuyerId { get; private set; }
    public string VaultId { get; private set; }
    public string? PayPalCustomerId { get; private set; }
    public string Brand { get; private set; }
    public string Last4 { get; private set; }
    public string Expiry { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeletedAt { get; private set; }

    public void Delete()
    {
        IsDeleted = true;
        DeletedAt = DateTimeOffset.UtcNow;
    }
}
