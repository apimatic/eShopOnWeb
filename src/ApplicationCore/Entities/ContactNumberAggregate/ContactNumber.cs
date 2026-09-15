using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

/// <summary>
/// A mobile number a shopper has put on file so the shop can reach them by SMS.
/// A number belongs to the shopper who registered it (<see cref="BuyerId"/>).
/// <see cref="PhoneNumber"/> holds the provider's own canonical E.164 form and is never written to logs.
/// </summary>
public class ContactNumber : BaseEntity, IAggregateRoot
{
    public string BuyerId { get; private set; }

    /// <summary>The provider's canonical E.164 form of the number (not whatever the caller typed).</summary>
    public string PhoneNumber { get; private set; }

    public DateTimeOffset RegisteredAt { get; private set; } = DateTimeOffset.UtcNow;

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
