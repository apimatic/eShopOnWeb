using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class BuyerContactNumber : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private BuyerContactNumber() { }
#pragma warning restore CS8618

    public BuyerContactNumber(string buyerId, string canonicalNumber)
    {
        BuyerId = buyerId;
        CanonicalNumber = canonicalNumber;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string BuyerId { get; private set; }
    public string CanonicalNumber { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
