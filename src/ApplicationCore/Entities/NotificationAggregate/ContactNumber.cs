using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A shopper's registered mobile contact number, stored in the provider's
/// canonical (E.164) form. Owned by exactly one shopper.
/// </summary>
public class ContactNumber : BaseEntity, IAggregateRoot
{
    public string OwnerId { get; private set; }
    public string PhoneNumber { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    #pragma warning disable CS8618 // Required by Entity Framework
    private ContactNumber() {}

    public ContactNumber(string ownerId, string phoneNumber)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(phoneNumber, nameof(phoneNumber));

        OwnerId = ownerId;
        PhoneNumber = phoneNumber;
        CreatedAt = DateTimeOffset.UtcNow;
    }
}
