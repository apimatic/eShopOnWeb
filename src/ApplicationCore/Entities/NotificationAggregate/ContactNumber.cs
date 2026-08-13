using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A mobile number a shopper has put on file so the shop can reach them by SMS.
/// The stored value is always the messaging provider's canonical E.164 form of the
/// number (obtained by validating it against the provider), never the raw caller input.
/// A number belongs to exactly one shopper (<see cref="OwnerId"/>).
/// </summary>
public class ContactNumber : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private ContactNumber() { }

    public ContactNumber(string ownerId, string e164Number)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(e164Number, nameof(e164Number));

        OwnerId = ownerId;
        E164Number = e164Number;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Identity (user name) of the shopper who owns this number.</summary>
    public string OwnerId { get; private set; }

    /// <summary>Provider-canonical E.164 phone number. Never written to logs.</summary>
    public string E164Number { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
