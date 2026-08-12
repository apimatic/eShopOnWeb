using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A mobile number a shopper has put on file so the shop can text them about their orders.
/// The stored <see cref="PhoneNumber"/> is always the provider's canonical E.164 form,
/// never the raw string the caller typed.
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
    }

    /// <summary>Identity of the shopper who registered this number (the JWT subject / username).</summary>
    public string OwnerId { get; private set; }

    /// <summary>The provider's canonical E.164 form of the number. Treated as sensitive: never logged.</summary>
    public string PhoneNumber { get; private set; }

    public DateTimeOffset RegisteredAt { get; private set; } = DateTimeOffset.UtcNow;
}
