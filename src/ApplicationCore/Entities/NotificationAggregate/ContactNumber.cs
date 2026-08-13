using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A mobile number a shopper has put on file so the shop can reach them by SMS.
/// The value stored in <see cref="PhoneNumber"/> is the provider's own canonical E.164 form,
/// not whatever the caller typed. A contact number is owned by the shopper who registered it
/// (<see cref="OwnerId"/>) and is never exposed to, used by, or deletable by another shopper.
/// The number itself is treated as sensitive and is never written to logs.
/// </summary>
public class ContactNumber : BaseEntity, IAggregateRoot
{
    public string OwnerId { get; private set; }

    /// <summary>Provider-canonical E.164 phone number (e.g. +14155552671).</summary>
    public string PhoneNumber { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; } = DateTimeOffset.UtcNow;

    #pragma warning disable CS8618 // Required by Entity Framework
    private ContactNumber() { }

    public ContactNumber(string ownerId, string phoneNumber)
    {
        OwnerId = Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        PhoneNumber = Guard.Against.NullOrEmpty(phoneNumber, nameof(phoneNumber));
    }
}
