using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A mobile number a shopper has put on file so the shop can reach them by text message.
/// <para>
/// The stored <see cref="PhoneNumber"/> is the provider's own canonical (E.164) form of the number,
/// not whatever the caller typed. The number is PII and is never written to logs.
/// </para>
/// A contact number belongs to the shopper who registered it (<see cref="BuyerId"/>); one shopper
/// must never see, use, or delete another's.
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

    /// <summary>The shopper who registered this number.</summary>
    public string BuyerId { get; private set; }

    /// <summary>The number in the provider's canonical E.164 form. PII — never logged.</summary>
    public string PhoneNumber { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; } = DateTimeOffset.UtcNow;
}
