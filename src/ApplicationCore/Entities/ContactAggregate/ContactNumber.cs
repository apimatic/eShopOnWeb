using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;

public class ContactNumber : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private ContactNumber() { }

    public ContactNumber(string buyerId, string phoneNumber, string? nationalFormat)
    {
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
