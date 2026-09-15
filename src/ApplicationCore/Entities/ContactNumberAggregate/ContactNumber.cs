using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

/// <summary>
/// A mobile contact number a shopper has put on file so the shop can reach them by SMS.
/// The stored value is always the messaging provider's own canonical (E.164) form of the
/// number, never the raw text the caller typed. A number belongs to exactly one shopper.
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
        RegisteredAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The shopper who owns this number (their identity/username). Used to scope every query.</summary>
    public string OwnerId { get; private set; }

    /// <summary>The provider-canonical E.164 form of the number (e.g. +15551234567).</summary>
    public string E164Number { get; private set; }

    public DateTimeOffset RegisteredAt { get; private set; }
}
