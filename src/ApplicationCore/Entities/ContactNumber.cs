using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class ContactNumber : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618
    private ContactNumber() { }

    public ContactNumber(string ownerId, string phoneNumber, DateTimeOffset createdAt)
    {
        OwnerId = Guard.Against.NullOrWhiteSpace(ownerId, nameof(ownerId));
        PhoneNumber = Guard.Against.NullOrWhiteSpace(phoneNumber, nameof(phoneNumber));
        CreatedAt = createdAt;
    }

    public string OwnerId { get; private set; }
    public string PhoneNumber { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public bool IsActive => DeletedAt is null;

    public void Delete(DateTimeOffset deletedAt)
    {
        DeletedAt ??= deletedAt;
    }
}
