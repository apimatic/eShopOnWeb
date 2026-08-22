using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;

public class ShopperContactNumber : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618
    private ShopperContactNumber() { }

    public ShopperContactNumber(string buyerId, string canonicalPhoneNumber)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(canonicalPhoneNumber, nameof(canonicalPhoneNumber));

        BuyerId = buyerId;
        PhoneNumber = canonicalPhoneNumber;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string BuyerId { get; private set; }
    public string PhoneNumber { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
