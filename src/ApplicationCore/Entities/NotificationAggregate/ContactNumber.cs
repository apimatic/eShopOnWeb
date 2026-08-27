using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public sealed class ContactNumber : BaseEntity, IAggregateRoot
{
    private ContactNumber() { }

    public ContactNumber(string buyerId, string canonicalNumber, DateTimeOffset createdAt)
    {
        BuyerId = buyerId;
        CanonicalNumber = canonicalNumber;
        CreatedAt = createdAt;
    }

    public string BuyerId { get; private set; } = string.Empty;
    public string CanonicalNumber { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public bool IsDeleted => DeletedAt.HasValue;

    public void Delete(DateTimeOffset deletedAt) => DeletedAt ??= deletedAt;
}
