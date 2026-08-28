using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;

public class ContactNumber : BaseEntity, IAggregateRoot
{
    private ContactNumber() { }

    public ContactNumber(string userId, string canonicalNumber)
    {
        UserId = Guard.Against.NullOrEmpty(userId, nameof(userId));
        CanonicalNumber = Guard.Against.NullOrEmpty(canonicalNumber, nameof(canonicalNumber));
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string UserId { get; private set; } = string.Empty;
    public string CanonicalNumber { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public bool IsActive => DeletedAt is null;

    public void Delete()
    {
        DeletedAt ??= DateTimeOffset.UtcNow;
    }
}
