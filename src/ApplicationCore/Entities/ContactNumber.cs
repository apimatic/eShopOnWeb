using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class ContactNumber : BaseEntity, IAggregateRoot
{
    private ContactNumber() { }

    public ContactNumber(string ownerId, string canonicalNumber)
    {
        OwnerId = Guard.Against.NullOrWhiteSpace(ownerId, nameof(ownerId));
        CanonicalNumber = Guard.Against.NullOrWhiteSpace(canonicalNumber, nameof(canonicalNumber));
    }

    public string OwnerId { get; private set; } = null!;
    public string CanonicalNumber { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
}
