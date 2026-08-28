using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class ContactNumber : BaseEntity, IAggregateRoot
{
    private ContactNumber() { }

    public ContactNumber(string buyerId, string value, DateTimeOffset createdAt)
    {
        BuyerId = Guard.Against.NullOrWhiteSpace(buyerId);
        Value = Guard.Against.NullOrWhiteSpace(value);
        CreatedAt = createdAt;
    }

    public string BuyerId { get; private set; } = string.Empty;
    public string Value { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? RemovedAt { get; private set; }
    public bool IsActive => RemovedAt is null;

    public void Remove(DateTimeOffset removedAt)
    {
        RemovedAt ??= removedAt;
    }
}
