using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public class ContactNumber : BaseEntity, IAggregateRoot
{
    private ContactNumber() { }

    public ContactNumber(string buyerId, string canonicalNumber)
    {
        BuyerId = buyerId;
        CanonicalNumber = canonicalNumber;
    }

    public string BuyerId { get; private set; } = null!;
    public string CanonicalNumber { get; private set; } = null!;
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RemovedAt { get; private set; }

    public void Remove()
    {
        if (!IsActive) return;
        IsActive = false;
        RemovedAt = DateTimeOffset.UtcNow;
    }
}
