using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public class ContactNumber : BaseEntity, IAggregateRoot
{
    private ContactNumber() { }

    public ContactNumber(string buyerId, string canonicalNumber, DateTimeOffset createdAt)
    {
        BuyerId = buyerId;
        CanonicalNumber = canonicalNumber;
        CreatedAt = createdAt;
    }

    public string BuyerId { get; private set; } = null!;
    public string CanonicalNumber { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? RemovedAt { get; private set; }
    public bool IsActive => RemovedAt == null;

    public void Remove(DateTimeOffset now)
    {
        RemovedAt ??= now;
    }
}
