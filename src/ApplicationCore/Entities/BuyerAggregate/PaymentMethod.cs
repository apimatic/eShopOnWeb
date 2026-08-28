using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

public class PaymentMethod : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() { }

    public PaymentMethod(string buyerId, string payPalVaultId, string brand, string last4, string? expiry)
    {
        BuyerId = Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        PayPalVaultId = Guard.Against.NullOrEmpty(payPalVaultId, nameof(payPalVaultId));
        Brand = Guard.Against.NullOrEmpty(brand, nameof(brand));
        Last4 = Guard.Against.NullOrEmpty(last4, nameof(last4));
        Expiry = expiry;
    }

    public string BuyerId { get; private set; }
    public string PayPalVaultId { get; private set; }
    public string Brand { get; private set; }
    public string Last4 { get; private set; }
    public string? Expiry { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeletedAt { get; private set; }
    public bool IsDeleted => DeletedAt.HasValue;

    public void MarkDeleted() => DeletedAt = DateTimeOffset.UtcNow;
}
