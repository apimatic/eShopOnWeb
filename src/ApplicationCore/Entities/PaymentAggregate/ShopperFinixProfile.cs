using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Links an eShop shopper to the Finix buyer Identity that owns their tokenized cards.
/// </summary>
public class ShopperFinixProfile : BaseEntity, IAggregateRoot
{
    // Required by Entity Framework.
    private ShopperFinixProfile()
    {
    }

    public ShopperFinixProfile(string buyerId, string finixIdentityId)
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
