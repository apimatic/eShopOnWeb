using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class ContactNumber : BaseEntity, IAggregateRoot
{
    private ContactNumber() { }

    public ContactNumber(string shopperId, string canonicalNumber, DateTimeOffset createdAt)
    {
        ShopperId = shopperId;
        CanonicalNumber = canonicalNumber;
        CreatedAt = createdAt;
    }

    public string ShopperId { get; private set; } = null!;
    public string CanonicalNumber { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public bool IsActive => DeletedAt is null;

    public void Delete(DateTimeOffset deletedAt)
    {
        DeletedAt ??= deletedAt;
    }

    public void Restore()
    {
        DeletedAt = null;
    }
}
