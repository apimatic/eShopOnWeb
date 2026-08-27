using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class ContactNumber : BaseEntity, IAggregateRoot
{
    private ContactNumber() { }

    public ContactNumber(string ownerId, string canonicalNumber, DateTimeOffset createdAt)
    {
        OwnerId = Guard.Against.NullOrWhiteSpace(ownerId, nameof(ownerId));
        CanonicalNumber = Guard.Against.NullOrWhiteSpace(canonicalNumber, nameof(canonicalNumber));
        CreatedAt = createdAt;
    }

    public string OwnerId { get; private set; } = string.Empty;
    public string CanonicalNumber { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
}
