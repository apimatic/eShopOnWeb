using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;

/// <summary>
/// A shopper's mobile destination, stored in the provider's canonical (E.164) form.
/// </summary>
public class ShopperContactNumber : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618
    private ShopperContactNumber() { }
    #pragma warning restore CS8618

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
