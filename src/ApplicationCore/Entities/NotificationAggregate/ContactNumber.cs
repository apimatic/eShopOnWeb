using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A mobile number a shopper has put on file so the shop can reach them by SMS.
/// Belongs to exactly one shopper (<see cref="BuyerId"/>) and stores the messaging
/// provider's canonical E.164 form of the number, not whatever the caller typed.
/// </summary>
public class ContactNumber : BaseEntity, IAggregateRoot
{
    public string BuyerId { get; private set; }

    /// <summary>The provider-canonical E.164 phone number. This is PII and must never be logged.</summary>
    public string PhoneNumber { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; } = DateTimeOffset.UtcNow;

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
}
