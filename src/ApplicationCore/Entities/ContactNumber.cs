using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public sealed class ContactNumber : BaseEntity, IAggregateRoot
{
    private ContactNumber() { }

    public ContactNumber(string buyerId, string canonicalNumber)
    {
        BuyerId = Guard.Against.NullOrWhiteSpace(buyerId);
        CanonicalNumber = Guard.Against.NullOrWhiteSpace(canonicalNumber);
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string BuyerId { get; private set; } = string.Empty;
    public string CanonicalNumber { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
}
