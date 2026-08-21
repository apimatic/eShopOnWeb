using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

/// <summary>
/// A shopper's registered mobile number, stored in the provider's canonical E.164 form.
/// </summary>
public class BuyerContactNumber : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618
    private BuyerContactNumber() { }
    #pragma warning restore CS8618

    public BuyerContactNumber(string buyerId, string phoneNumber, string? nationalFormat)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(phoneNumber, nameof(phoneNumber));

        BuyerId = buyerId;
        PhoneNumber = phoneNumber;
        NationalFormat = nationalFormat;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string BuyerId { get; private set; }
    public string PhoneNumber { get; private set; }
    public string? NationalFormat { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
