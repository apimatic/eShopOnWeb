using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class ContactNumber : BaseEntity, IAggregateRoot
{
    private ContactNumber() { }

    public ContactNumber(string buyerId, string canonicalNumber, DateTimeOffset createdAt)
    {
        BuyerId = buyerId;
        Number = canonicalNumber;
        CreatedAt = createdAt;
    }

    public string BuyerId { get; private set; } = string.Empty;
    public string Number { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public bool IsActive => DeletedAt == null;

    public void Delete(DateTimeOffset now)
    {
        if (DeletedAt != null) return;
        DeletedAt = now;
        Number = string.Empty;
    }
}
