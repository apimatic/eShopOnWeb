using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A mobile number a shopper has put on file so the shop can text them about their orders.
/// The stored <see cref="PhoneNumber"/> is the provider's own canonical E.164 form, established at
/// registration time — not whatever the caller typed. A number is owned by exactly one shopper.
/// </summary>
public class ContactNumber : BaseEntity, IAggregateRoot
{
    /// <summary>Identity (user name) of the shopper who owns this number.</summary>
    public string OwnerId { get; private set; }

    /// <summary>The provider's canonical E.164 form of the number. Treated as personal data — never logged.</summary>
    public string PhoneNumber { get; private set; }

    public System.DateTimeOffset RegisteredAt { get; private set; } = System.DateTimeOffset.UtcNow;

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
}
