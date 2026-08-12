using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A mobile number a shopper has put on file so the shop can text them.
/// The stored <see cref="PhoneNumber"/> is always the provider's canonical E.164 form,
/// not whatever the caller typed. Belongs to exactly one shopper (<see cref="BuyerId"/>).
/// The number value itself is sensitive: it is never written to logs.
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

    /// <summary>Identity of the owning shopper (JWT username). Not shared across shoppers.</summary>
    public string BuyerId { get; private set; }

    /// <summary>Canonical E.164 number as validated and returned by the provider.</summary>
    public string PhoneNumber { get; private set; }

    public DateTimeOffset RegisteredAt { get; private set; } = DateTimeOffset.UtcNow;
}
