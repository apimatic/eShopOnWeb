using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A mobile number a shopper has put on file so the shop can reach them by SMS.
/// The stored <see cref="PhoneNumber"/> is always the provider's canonical E.164 form,
/// never whatever the caller typed. A number belongs to exactly one shopper.
/// </summary>
public class ContactNumber : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private ContactNumber() { }
#pragma warning restore CS8618

    public ContactNumber(string ownerId, string phoneNumberE164)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(phoneNumberE164, nameof(phoneNumberE164));

        OwnerId = ownerId;
        PhoneNumber = phoneNumberE164;
    }

    /// <summary>The shopper who registered this number (their user name / identity from the token).</summary>
    public string OwnerId { get; private set; }

    /// <summary>Canonical E.164 number as confirmed by the messaging provider. Never written to logs.</summary>
    public string PhoneNumber { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
}
