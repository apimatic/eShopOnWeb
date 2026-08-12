using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A mobile number a shopper has put on file so the shop can text them about their
/// orders. The stored <see cref="PhoneNumber"/> is always the provider's canonical
/// E.164 form, established by a provider lookup at registration time — never the raw
/// string the caller typed.
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

    /// <summary>Identity of the shopper who registered this number (the token's user name).</summary>
    public string OwnerId { get; private set; }

    /// <summary>Canonical E.164 number as returned by the provider's lookup.</summary>
    public string PhoneNumber { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; }
}
