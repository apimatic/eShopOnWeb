using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A mobile number a shopper has put on file so the shop can reach them by SMS.
/// The stored <see cref="PhoneNumber"/> is always the provider's canonical E.164 form,
/// validated at registration time. The value is sensitive and must never be written to logs.
/// </summary>
public class ContactNumber : BaseEntity, IAggregateRoot
{
    /// <summary>
    /// The identity of the shopper who registered this number (their user name / token identity).
    /// A number belongs to its owner: it is only ever listed, used or deleted for that shopper.
    /// </summary>
    public string OwnerId { get; private set; }

    /// <summary>
    /// The provider's canonical E.164 representation of the number (e.g. <c>+15551234567</c>).
    /// </summary>
    public string PhoneNumber { get; private set; }

    public DateTimeOffset RegisteredAt { get; private set; } = DateTimeOffset.UtcNow;

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
