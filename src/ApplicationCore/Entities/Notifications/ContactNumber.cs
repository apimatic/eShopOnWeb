using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;

public class ContactNumber : BaseEntity, IAggregateRoot
{
    private ContactNumber() { }

    public ContactNumber(string buyerId, string canonicalNumber, DateTimeOffset createdAt)
    {
        BuyerId = Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        CanonicalNumber = Guard.Against.NullOrEmpty(canonicalNumber, nameof(canonicalNumber));
        CreatedAt = createdAt;
    }

    public string BuyerId { get; private set; } = string.Empty;
    public string CanonicalNumber { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
}
