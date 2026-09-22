using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A mobile number a shopper has put on file so the shop can reach them. The stored
/// <see cref="PhoneNumber"/> is the provider's own canonical (E.164) form, not whatever the
/// caller typed. A number belongs to exactly one shopper (<see cref="OwnerId"/>).
/// </summary>
public class ContactNumber : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private ContactNumber() { }

    public ContactNumber(string ownerId, string phoneNumber)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(phoneNumber, nameof(phoneNumber));

        OwnerId = ownerId;
        PhoneNumber = phoneNumber;
        RegisteredAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The shopper this number belongs to (the caller's token identity).</summary>
    public string OwnerId { get; private set; }

    /// <summary>The provider's canonical E.164 form of the number.</summary>
    public string PhoneNumber { get; private set; }

    public DateTimeOffset RegisteredAt { get; private set; }
}
