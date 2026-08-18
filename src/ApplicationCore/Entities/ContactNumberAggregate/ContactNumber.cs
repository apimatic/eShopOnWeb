using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

/// <summary>
/// A mobile number a shopper has registered so the shop can reach them by SMS.
/// The stored <see cref="PhoneNumber"/> is always the provider's canonical (E.164) form,
/// never the raw string the caller typed. A number belongs to exactly one shopper
/// (<see cref="BuyerId"/>) and must never be exposed to, used by, or deleted by another.
/// </summary>
public class ContactNumber : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private ContactNumber() { }
#pragma warning restore CS8618

    public ContactNumber(string buyerId, string phoneNumber)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(phoneNumber, nameof(phoneNumber));

        BuyerId = buyerId;
        PhoneNumber = phoneNumber;
    }

    /// <summary>Owner of this number (the authenticated shopper's identity).</summary>
    public string BuyerId { get; private set; }

    /// <summary>Provider-canonical E.164 form of the number. Never written to logs.</summary>
    public string PhoneNumber { get; private set; }

    public DateTimeOffset RegisteredAt { get; private set; } = DateTimeOffset.UtcNow;
}
