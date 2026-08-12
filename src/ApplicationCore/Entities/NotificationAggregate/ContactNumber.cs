using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A mobile number a shopper has put on file so the shop can text them about their orders.
/// The stored <see cref="PhoneNumber"/> is always the provider's canonical E.164 form, never the
/// raw string the caller typed. The number is PII and must never be written to logs.
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
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Identity of the shopper that owns this number (the JWT name claim).</summary>
    public string BuyerId { get; private set; }

    /// <summary>The provider's canonical E.164 form of the number. PII — never log this.</summary>
    public string PhoneNumber { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
