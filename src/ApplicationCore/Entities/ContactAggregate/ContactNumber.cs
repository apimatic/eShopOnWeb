using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;

public class ContactNumber : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618
    private ContactNumber() { }
#pragma warning restore CS8618

    public ContactNumber(string buyerId, string phoneNumber, string? nationalFormat, string? lineType)
    {
        BuyerId = buyerId;
        PhoneNumber = phoneNumber;
        NationalFormat = nationalFormat;
        LineType = lineType;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string BuyerId { get; private set; }
    public string PhoneNumber { get; private set; }
    public string? NationalFormat { get; private set; }
    public string? LineType { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    public bool IsDeleted => DeletedAt.HasValue;

    public void Delete()
    {
        DeletedAt ??= DateTimeOffset.UtcNow;
    }

    public void Restore(string? nationalFormat, string? lineType)
    {
        DeletedAt = null;
        NationalFormat = nationalFormat;
        LineType = lineType;
        CreatedAt = DateTimeOffset.UtcNow;
    }
}
