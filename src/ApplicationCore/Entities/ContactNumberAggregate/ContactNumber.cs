using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

/// <summary>
/// A mobile number a shopper has put on file so the shop can reach them by SMS.
/// A ContactNumber belongs to exactly one shopper (<see cref="OwnerId"/>); one shopper must never
/// see, use, or delete another's. The value stored in <see cref="PhoneNumber"/> is always the
/// provider's own canonical E.164 form, never the raw text the caller typed.
/// </summary>
public class ContactNumber : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private ContactNumber() { }
#pragma warning restore CS8618

    public ContactNumber(string ownerId, string phoneNumber)
    {
        OwnerId = Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        PhoneNumber = Guard.Against.NullOrEmpty(phoneNumber, nameof(phoneNumber));
    }

    /// <summary>Identity of the shopper who registered this number (the JWT subject / user name).</summary>
    public string OwnerId { get; private set; }

    /// <summary>The provider's canonical E.164 representation of the number. Never written to logs.</summary>
    public string PhoneNumber { get; private set; }

    public DateTimeOffset RegisteredAt { get; private set; } = DateTimeOffset.UtcNow;
}
