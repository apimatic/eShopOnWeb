using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A mobile number a shopper has put on file so the shop can reach them by SMS.
/// The stored value is the provider's canonical (E.164) form, never the raw caller input.
/// A number belongs to exactly one shopper (<see cref="OwnerId"/>).
/// </summary>
public class ContactNumber : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private ContactNumber() { }

    public ContactNumber(string ownerId, string e164PhoneNumber)
    {
        OwnerId = Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        PhoneNumber = Guard.Against.NullOrEmpty(e164PhoneNumber, nameof(e164PhoneNumber));
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Identity (JWT name) of the shopper who owns this number.</summary>
    public string OwnerId { get; private set; }

    /// <summary>The provider's canonical E.164 form of the number. Never written to logs.</summary>
    public string PhoneNumber { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
