using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class ContactNumber : BaseEntity, IAggregateRoot
{
    private ContactNumber() { }

    public ContactNumber(string shopperId, string canonicalNumber)
    {
        ShopperId = string.IsNullOrWhiteSpace(shopperId)
            ? throw new ArgumentException("A shopper is required.", nameof(shopperId))
            : shopperId;
        CanonicalNumber = string.IsNullOrWhiteSpace(canonicalNumber)
            ? throw new ArgumentException("A canonical number is required.", nameof(canonicalNumber))
            : canonicalNumber;
    }

    public string ShopperId { get; private set; } = null!;
    public string CanonicalNumber { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
}
