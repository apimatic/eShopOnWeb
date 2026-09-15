using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A mobile number a shopper has put on file so the shop can reach them by SMS.
/// The stored value is the provider's canonical (E.164) form of the number, never
/// whatever the caller typed. A number belongs to exactly one shopper (<see cref="OwnerId"/>).
/// The number itself is treated as personal data and must never be written to logs.
/// </summary>
public class ContactNumber : BaseEntity, IAggregateRoot
{
    private ContactNumber() { } // EF only

    public ContactNumber(string ownerId, string phoneNumber)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(phoneNumber, nameof(phoneNumber));

        OwnerId = ownerId;
        PhoneNumber = phoneNumber;
    }

    /// <summary>Identity (username / email) of the shopper who registered this number.</summary>
    public string OwnerId { get; private set; } = default!;

    /// <summary>The provider's canonical E.164 form of the number.</summary>
    public string PhoneNumber { get; private set; } = default!;

    public DateTimeOffset RegisteredAt { get; private set; } = DateTimeOffset.UtcNow;
}
