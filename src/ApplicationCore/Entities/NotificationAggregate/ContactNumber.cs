using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A mobile contact number a shopper has put on file so the shop can reach them by SMS.
/// The stored value is always the provider's canonical (E.164) form of the number.
/// A number belongs to exactly one shopper (<see cref="OwnerId"/>); it is never shared,
/// and its value is never written to logs.
/// </summary>
public class ContactNumber : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private ContactNumber() { }
#pragma warning restore CS8618

    public ContactNumber(string ownerId, string canonicalNumber)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(canonicalNumber, nameof(canonicalNumber));

        OwnerId = ownerId;
        CanonicalNumber = canonicalNumber;
        RegisteredDate = DateTimeOffset.UtcNow;
    }

    /// <summary>Identity (username) of the shopper who registered this number.</summary>
    public string OwnerId { get; private set; }

    /// <summary>The provider's canonical E.164 form of the number. Never logged.</summary>
    public string CanonicalNumber { get; private set; }

    public DateTimeOffset RegisteredDate { get; private set; }
}
