using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A mobile number a shopper has put on file so the shop can reach them by SMS.
/// The stored <see cref="PhoneNumber"/> is the provider's own canonical (E.164) form, not caller input.
/// The number is personal data: it is never written to logs.
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
        RegisteredAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The shopper who owns this number (their token identity). Scopes every read/delete.</summary>
    public string BuyerId { get; private set; }

    /// <summary>Provider-canonical E.164 destination. Personal data — never logged.</summary>
    public string PhoneNumber { get; private set; }

    public DateTimeOffset RegisteredAt { get; private set; }
}
