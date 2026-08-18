using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A mobile contact number a shopper has put on file so the shop can reach them by SMS.
/// The number stored here is always the provider's canonical (E.164) form, never the raw caller input.
/// A contact number belongs to exactly one shopper (<see cref="BuyerId"/>).
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

    /// <summary>The owning shopper's identity (username), matching <c>Order.BuyerId</c>.</summary>
    public string BuyerId { get; private set; }

    /// <summary>The canonical E.164 number as returned by the provider's Lookup. This is a personal contact
    /// detail and must never be written to logs.</summary>
    public string PhoneNumber { get; private set; }

    public DateTimeOffset RegisteredDate { get; private set; } = DateTimeOffset.UtcNow;
}
