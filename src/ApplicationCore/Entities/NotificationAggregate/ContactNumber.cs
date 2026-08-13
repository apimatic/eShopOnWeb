using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A mobile number a shopper has put on file so the shop can reach them by SMS.
/// The stored <see cref="PhoneNumber"/> is always the provider's canonical E.164 form,
/// not whatever the caller typed. A contact number belongs to exactly one shopper
/// (<see cref="OwnerId"/>); it is never visible to, usable by, or deletable by another.
/// </summary>
public class ContactNumber : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private ContactNumber() { }
#pragma warning restore CS8618

    public ContactNumber(string ownerId, string phoneNumber)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(phoneNumber, nameof(phoneNumber));

        OwnerId = ownerId;
        PhoneNumber = phoneNumber;
        RegisteredAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The identity (buyer id) of the shopper who registered this number.</summary>
    public string OwnerId { get; private set; }

    /// <summary>
    /// The number in the provider's canonical E.164 form. Treated as PII: it is never written to logs.
    /// </summary>
    public string PhoneNumber { get; private set; }

    public DateTimeOffset RegisteredAt { get; private set; }
}
