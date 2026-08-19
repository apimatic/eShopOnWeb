using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A mobile number a shopper has put on file so the shop can reach them by SMS.
/// The stored value is always the provider's canonical E.164 form, never the raw
/// text the caller typed. A number belongs to exactly one shopper (<see cref="OwnerId"/>).
/// </summary>
public class ContactNumber : BaseEntity, IAggregateRoot
{
    /// <summary>Identity of the shopper who registered this number (their username / email).</summary>
    public string OwnerId { get; private set; }

    /// <summary>The provider's canonical E.164 form of the number.</summary>
    public string PhoneNumber { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; } = DateTimeOffset.UtcNow;

    private ContactNumber()
    {
        // Required by EF Core.
        OwnerId = null!;
        PhoneNumber = null!;
    }

    public ContactNumber(string ownerId, string phoneNumber)
    {
        OwnerId = Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        PhoneNumber = Guard.Against.NullOrEmpty(phoneNumber, nameof(phoneNumber));
    }
}
