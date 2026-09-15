using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A mobile number a shopper has put on file so the shop can text them. A contact number belongs to
/// exactly one shopper (<see cref="OwnerId"/>); another shopper must never see, use or delete it.
/// The stored <see cref="PhoneNumber"/> is always the provider's canonical (E.164) form of whatever
/// the caller typed.
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
        CreatedDate = DateTimeOffset.UtcNow;
    }

    /// <summary>Identity (username / email) of the shopper who registered this number.</summary>
    public string OwnerId { get; private set; }

    /// <summary>The provider's canonical E.164 form of the number.</summary>
    public string PhoneNumber { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; }
}
