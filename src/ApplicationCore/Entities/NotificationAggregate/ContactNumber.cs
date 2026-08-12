using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A mobile number a shopper has registered so the shop can text them about their orders.
/// The stored <see cref="PhoneNumber"/> is always the provider's canonical E.164 form,
/// never the raw value the caller typed. A number belongs to exactly one shopper
/// (<see cref="OwnerId"/>) and is never written to logs.
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

    /// <summary>The shopper who owns this number (their identity/user name).</summary>
    public string OwnerId { get; private set; }

    /// <summary>Canonical E.164 phone number as returned by the provider's lookup.</summary>
    public string PhoneNumber { get; private set; }

    public DateTimeOffset RegisteredAt { get; private set; }
}
