using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;

/// <summary>
/// A mobile number a shopper has put on file so the shop can reach them by SMS.
/// The stored <see cref="Number"/> is always the provider's canonical E.164 form, never the raw
/// value the caller typed. A contact number belongs to exactly one shopper (<see cref="OwnerId"/>).
/// </summary>
public class ContactNumber : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private ContactNumber() { }
#pragma warning restore CS8618

    public ContactNumber(string ownerId, string number)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(number, nameof(number));

        OwnerId = ownerId;
        Number = number;
        RegisteredDate = DateTimeOffset.UtcNow;
    }

    /// <summary>Identity of the shopper who owns this number (the JWT name claim).</summary>
    public string OwnerId { get; private set; }

    /// <summary>Provider-canonical E.164 phone number.</summary>
    public string Number { get; private set; }

    public DateTimeOffset RegisteredDate { get; private set; }
}
