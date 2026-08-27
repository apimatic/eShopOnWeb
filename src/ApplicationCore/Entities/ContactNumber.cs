using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class ContactNumber : BaseEntity, IAggregateRoot
{
    private ContactNumber() { }

    public ContactNumber(string buyerId, string phoneNumber, DateTimeOffset createdAt)
    {
        BuyerId = Guard.Against.NullOrWhiteSpace(buyerId);
        PhoneNumber = Guard.Against.NullOrWhiteSpace(phoneNumber);
        CreatedAt = createdAt;
    }

    public string BuyerId { get; private set; } = string.Empty;
    public string PhoneNumber { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public bool IsActive => DeletedAt == null;

    public void Restore() => DeletedAt = null;
    public void Remove(DateTimeOffset removedAt) => DeletedAt = removedAt;
}
