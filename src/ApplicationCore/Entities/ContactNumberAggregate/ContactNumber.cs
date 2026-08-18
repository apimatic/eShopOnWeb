using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

/// <summary>
/// A mobile number a shopper has put on file so the shop can reach them by SMS.
/// <see cref="PhoneNumber"/> is the provider's canonical (E.164) form of the number and is
/// personally identifiable information: it must never be written to logs.
/// A contact number always belongs to exactly one shopper (<see cref="BuyerId"/>).
/// </summary>
public class ContactNumber : BaseEntity, IAggregateRoot
{
    public string BuyerId { get; private set; }

    /// <summary>The provider's canonical E.164 form of the number. PII &ndash; never log this.</summary>
    public string PhoneNumber { get; private set; }

    public DateTimeOffset DateRegistered { get; private set; } = DateTimeOffset.UtcNow;

#pragma warning disable CS8618 // Required by Entity Framework
    private ContactNumber() { }
#pragma warning restore CS8618

    public ContactNumber(string buyerId, string phoneNumber)
    {
        BuyerId = Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        PhoneNumber = Guard.Against.NullOrEmpty(phoneNumber, nameof(phoneNumber));
    }
}
