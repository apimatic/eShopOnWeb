using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A mobile number a shopper has put on file so the shop can reach them by SMS. The stored value is
/// the messaging provider's canonical E.164 form of the number, established when it was registered.
/// A contact number belongs to exactly one shopper (<see cref="OwnerId"/>).
/// </summary>
public class ContactNumber : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private ContactNumber() { }

    public ContactNumber(string ownerId, string phoneNumberE164)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(phoneNumberE164, nameof(phoneNumberE164));

        OwnerId = ownerId;
        PhoneNumber = phoneNumberE164;
        RegisteredAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Identity of the shopper who registered this number (the JWT subject / user name).</summary>
    public string OwnerId { get; private set; }

    /// <summary>The provider's canonical E.164 representation of the number. Never written to logs.</summary>
    public string PhoneNumber { get; private set; }

    public DateTimeOffset RegisteredAt { get; private set; }
}
