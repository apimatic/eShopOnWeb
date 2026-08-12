using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A mobile number a shopper has put on file so the shop can reach them. The stored value is the
/// provider's own canonical (E.164) form of the number, established at registration time. A
/// contact number belongs to exactly one shopper.
/// </summary>
public class ContactNumber : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private ContactNumber() { }
#pragma warning restore CS8618

    public ContactNumber(string ownerId, string e164Number)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(e164Number, nameof(e164Number));

        OwnerId = ownerId;
        E164Number = e164Number;
    }

    /// <summary>Identity of the shopper who owns this number (their user name).</summary>
    public string OwnerId { get; private set; }

    /// <summary>
    /// The provider's canonical E.164 form of the number. Treated as a shopper contact detail:
    /// it is never written to logs.
    /// </summary>
    public string E164Number { get; private set; }

    public DateTimeOffset RegisteredAt { get; private set; } = DateTimeOffset.UtcNow;
}
