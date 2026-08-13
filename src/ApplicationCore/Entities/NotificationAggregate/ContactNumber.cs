using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A mobile number a shopper has put on file so the shop can reach them by SMS.
/// The value stored is the provider's own canonical (E.164) form of the number, never the raw
/// string the caller typed. A contact number belongs to exactly one shopper (<see cref="BuyerId"/>).
/// </summary>
public class ContactNumber : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private ContactNumber() { }
#pragma warning restore CS8618

    public ContactNumber(string buyerId, string canonicalNumber)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(canonicalNumber, nameof(canonicalNumber));

        BuyerId = buyerId;
        CanonicalNumber = canonicalNumber;
        RegisteredAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The identity (username) of the shopper who owns this number.</summary>
    public string BuyerId { get; private set; }

    /// <summary>The provider's canonical E.164 form of the number. Treated as a secret contact detail — never logged.</summary>
    public string CanonicalNumber { get; private set; }

    public DateTimeOffset RegisteredAt { get; private set; }
}
