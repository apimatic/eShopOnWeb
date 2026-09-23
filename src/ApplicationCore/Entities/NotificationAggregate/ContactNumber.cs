using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A mobile number a shopper has put on file. <see cref="PhoneNumber"/> is the provider's own canonical
/// E.164 form (from a Lookup), never whatever the caller typed. The number is sensitive and must never be logged.
/// </summary>
public class ContactNumber : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private ContactNumber() { }

    public ContactNumber(string ownerId, string phoneNumber)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(phoneNumber, nameof(phoneNumber));
        OwnerId = ownerId;
        PhoneNumber = phoneNumber;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Identity (username) of the shopper who owns this number. Scopes all access.</summary>
    public string OwnerId { get; private set; }

    /// <summary>Canonical E.164 number as returned by the provider's Lookup. Sensitive — never log this.</summary>
    public string PhoneNumber { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
