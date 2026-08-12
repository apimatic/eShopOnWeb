using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

/// <summary>
/// A mobile number a shopper has put on file so the shop can reach them by SMS.
/// The stored value is always the messaging provider's own canonical E.164 form, produced by
/// a provider lookup at registration time — never the raw string the caller typed.
/// A number belongs to exactly one shopper (<see cref="BuyerId"/>).
/// </summary>
public class ContactNumber : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private ContactNumber() { }
#pragma warning restore CS8618

    public ContactNumber(string buyerId, string phoneNumberE164)
    {
        BuyerId = Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        PhoneNumber = Guard.Against.NullOrEmpty(phoneNumberE164, nameof(phoneNumberE164));
        RegisteredAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Identity username of the shopper who owns this number.</summary>
    public string BuyerId { get; private set; }

    /// <summary>The provider's canonical E.164 form of the number. Never written to logs.</summary>
    public string PhoneNumber { get; private set; }

    public DateTimeOffset RegisteredAt { get; private set; }
}
