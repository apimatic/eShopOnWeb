using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A mobile number a shopper has put on file so the shop can reach them by SMS.
/// The stored value is always the provider's canonical E.164 form of the number,
/// never the raw text the caller typed.
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
        PhoneNumberE164 = phoneNumberE164;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Identity (username) of the shopper who registered this number.</summary>
    public string OwnerId { get; private set; }

    /// <summary>Canonical E.164 number as returned by the provider's Lookup.</summary>
    public string PhoneNumberE164 { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
