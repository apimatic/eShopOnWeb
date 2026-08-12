using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A mobile contact number a shopper has put on file so the shop can reach them by SMS.
/// The stored <see cref="PhoneNumber"/> is always the provider's canonical E.164 form,
/// not whatever the caller originally typed.
/// A contact number belongs to exactly one shopper (<see cref="BuyerId"/>).
/// </summary>
public class ContactNumber : BaseEntity, IAggregateRoot
{
    private ContactNumber() { } // EF

    public ContactNumber(string buyerId, string phoneNumber)
    {
        BuyerId = Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        PhoneNumber = Guard.Against.NullOrEmpty(phoneNumber, nameof(phoneNumber));
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The owning shopper's identity (the JWT subject / username).</summary>
    public string BuyerId { get; private set; } = default!;

    /// <summary>The provider-canonical E.164 number. Treated as PII; never written to logs.</summary>
    public string PhoneNumber { get; private set; } = default!;

    public DateTimeOffset CreatedAt { get; private set; }
}
