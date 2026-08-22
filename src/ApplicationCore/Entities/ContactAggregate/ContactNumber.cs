using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;

public class ContactNumber : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618
    private ContactNumber() { }

    public ContactNumber(string buyerId, string phoneNumber, string? nationalFormat, string? countryCode)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(phoneNumber, nameof(phoneNumber));

        BuyerId = buyerId;
        PhoneNumber = phoneNumber;
        NationalFormat = nationalFormat;
        CountryCode = countryCode;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string BuyerId { get; private set; }
    public string PhoneNumber { get; private set; }
    public string? NationalFormat { get; private set; }
    public string? CountryCode { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? RemovedAt { get; private set; }

    public bool IsRemoved => RemovedAt.HasValue;

    public void Remove()
    {
        RemovedAt ??= DateTimeOffset.UtcNow;
    }

    public void Restore()
    {
        RemovedAt = null;
    }
}
