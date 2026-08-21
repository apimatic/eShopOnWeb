using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class ShopperContactNumber : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618
    private ShopperContactNumber() { }
#pragma warning restore CS8618

    public ShopperContactNumber(string buyerId, string canonicalNumber)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(canonicalNumber, nameof(canonicalNumber));

        BuyerId = buyerId;
        CanonicalNumber = canonicalNumber;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string BuyerId { get; private set; }
    public string CanonicalNumber { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    public void Deactivate()
    {
        if (IsDeleted)
        {
            return;
        }

        IsDeleted = true;
        DeletedAt = DateTimeOffset.UtcNow;
    }

    public void Reactivate(string canonicalNumber)
    {
        Guard.Against.NullOrEmpty(canonicalNumber, nameof(canonicalNumber));
        CanonicalNumber = canonicalNumber;
        IsDeleted = false;
        DeletedAt = null;
    }
}
