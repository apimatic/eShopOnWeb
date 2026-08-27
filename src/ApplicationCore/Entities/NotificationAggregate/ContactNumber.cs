using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A shopper's registered mobile number, stored in the provider's canonical (E.164) form.
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

    /// <summary>
    /// The identity (user name) of the shopper who registered this number.
    /// </summary>
    public string OwnerId { get; private set; }

    /// <summary>
    /// Canonical E.164 form of the number as returned by the provider.
    /// Never write this value to logs.
    /// </summary>
    public string PhoneNumber { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
