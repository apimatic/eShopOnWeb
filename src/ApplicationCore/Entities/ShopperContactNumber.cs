using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class ShopperContactNumber : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618
    private ShopperContactNumber() { }
    #pragma warning restore CS8618

    public ShopperContactNumber(string buyerId, string phoneNumber, string? nationalFormat)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(phoneNumber, nameof(phoneNumber));

        BuyerId = buyerId;
        PhoneNumber = phoneNumber;
        NationalFormat = nationalFormat;
        RegisteredAt = DateTimeOffset.UtcNow;
    }

    public string BuyerId { get; private set; }
    public string PhoneNumber { get; private set; }
    public string? NationalFormat { get; private set; }
    public DateTimeOffset RegisteredAt { get; private set; }
}
