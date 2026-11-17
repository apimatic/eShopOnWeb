using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

#pragma warning disable CS8618 // Required by Entity Framework

public class ShopperProfile : BaseEntity, IAggregateRoot
{
    private ShopperProfile() { }

    public ShopperProfile(string buyerId, string finixIdentityId)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(finixIdentityId, nameof(finixIdentityId));

        BuyerId = buyerId;
        FinixIdentityId = finixIdentityId;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string BuyerId { get; private set; }
    public string FinixIdentityId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
