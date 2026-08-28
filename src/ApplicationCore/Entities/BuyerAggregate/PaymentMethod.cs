using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

public class PaymentMethod : BaseEntity, IAggregateRoot
{
    private PaymentMethod() { }

    public PaymentMethod(string ownerId, string vaultId, string brand, string last4, string? expiry,
        DateTimeOffset createdAt)
    {
        OwnerId = Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        VaultId = Guard.Against.NullOrEmpty(vaultId, nameof(vaultId));
        Brand = Guard.Against.NullOrEmpty(brand, nameof(brand));
        Last4 = Guard.Against.NullOrEmpty(last4, nameof(last4));
        Expiry = expiry;
        CreatedAt = createdAt;
    }

    public string OwnerId { get; private set; } = null!;
    public string VaultId { get; private set; } = null!;
    public string Brand { get; private set; } = null!;
    public string Last4 { get; private set; } = null!;
    public string? Expiry { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
