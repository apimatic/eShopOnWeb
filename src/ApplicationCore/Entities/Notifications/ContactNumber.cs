using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;

public class ContactNumber : BaseEntity, IAggregateRoot
{
    private ContactNumber() { }

    public ContactNumber(string buyerId, string e164Number, DateTimeOffset createdAt)
    {
        BuyerId = buyerId;
        E164Number = e164Number;
        CreatedAt = createdAt;
    }

    public string BuyerId { get; private set; } = string.Empty;
    public string E164Number { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    public void Remove(DateTimeOffset deletedAt) => DeletedAt = deletedAt;
    public void Restore(DateTimeOffset createdAt)
    {
        DeletedAt = null;
        CreatedAt = createdAt;
    }
}
