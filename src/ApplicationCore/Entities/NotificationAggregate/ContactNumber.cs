using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public class ContactNumber : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private ContactNumber() { }

    public ContactNumber(string buyerId, string e164Number, DateTimeOffset createdAt)
    {
        BuyerId = buyerId;
        E164Number = e164Number;
        CreatedAt = createdAt;
    }

    public string BuyerId { get; private set; }
    public string E164Number { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public bool IsActive => DeletedAt is null;

    public void Remove(DateTimeOffset removedAt)
    {
        DeletedAt ??= removedAt;
    }
}
