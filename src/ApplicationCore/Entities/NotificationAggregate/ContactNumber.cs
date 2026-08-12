using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A mobile number a shopper has put on file so the shop can reach them by text. The stored
/// <see cref="PhoneNumber"/> is the provider's own canonical E.164 form, established at
/// registration time — not whatever the caller typed. A number belongs to exactly one shopper
/// (<see cref="BuyerId"/>) and is never written to logs.
/// </summary>
public class ContactNumber : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private ContactNumber() { }

    public ContactNumber(string buyerId, string phoneNumber)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(phoneNumber, nameof(phoneNumber));

        BuyerId = buyerId;
        PhoneNumber = phoneNumber;
    }

    /// <summary>Identity of the shopper who owns this number (the JWT name claim).</summary>
    public string BuyerId { get; private set; }

    /// <summary>The provider's canonical E.164 form of the number. Never logged.</summary>
    public string PhoneNumber { get; private set; }

    public DateTimeOffset RegisteredAt { get; private set; } = DateTimeOffset.UtcNow;
}
