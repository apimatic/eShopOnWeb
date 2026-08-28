using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class ContactNumber : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618
    private ContactNumber() { }

    public ContactNumber(string buyerId, string phoneNumber, DateTimeOffset createdAt)
    {
        BuyerId = buyerId;
        PhoneNumber = phoneNumber;
        CreatedAt = createdAt;
    }

    public string BuyerId { get; private set; }
    public string PhoneNumber { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? RemovedAt { get; private set; }
    public bool IsActive => RemovedAt is null;

    public void Remove(DateTimeOffset removedAt) => RemovedAt ??= removedAt;
}
