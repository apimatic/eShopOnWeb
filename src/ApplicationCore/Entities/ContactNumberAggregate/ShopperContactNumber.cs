using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

public class ShopperContactNumber : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private ShopperContactNumber() { }

    public ShopperContactNumber(string buyerId, string canonicalNumber, string? nationalFormat)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(canonicalNumber, nameof(canonicalNumber));

        BuyerId = buyerId;
        CanonicalNumber = canonicalNumber;
        NationalFormat = nationalFormat;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string BuyerId { get; private set; }
    public string CanonicalNumber { get; private set; }
    public string? NationalFormat { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
