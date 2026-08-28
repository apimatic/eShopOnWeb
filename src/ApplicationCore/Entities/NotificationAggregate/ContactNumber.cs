using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public sealed class ContactNumber : IAggregateRoot
{
    private ContactNumber() { }

    public ContactNumber(string shopperId, string canonicalNumber, DateTimeOffset createdAt)
    {
        Id = Guid.NewGuid();
        ShopperId = shopperId;
        CanonicalNumber = canonicalNumber;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public string ShopperId { get; private set; } = string.Empty;
    public string CanonicalNumber { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public byte[]? RowVersion { get; private set; }
    public bool IsActive => DeletedAt is null;

    public void Delete(DateTimeOffset deletedAt)
    {
        DeletedAt ??= deletedAt;
    }
}
