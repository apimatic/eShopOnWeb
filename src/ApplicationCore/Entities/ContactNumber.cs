using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class ContactNumber : BaseEntity, IAggregateRoot
{
    private ContactNumber() { }

    public ContactNumber(string buyerId, string e164Number, DateTimeOffset createdAt)
    {
        BuyerId = buyerId;
        E164Number = e164Number;
        CreatedAt = createdAt;
    }

    public string BuyerId { get; private set; } = null!;
    public string E164Number { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public bool IsActive => DeletedAt == null;

    public void Delete(DateTimeOffset deletedAt) => DeletedAt ??= deletedAt;
}
