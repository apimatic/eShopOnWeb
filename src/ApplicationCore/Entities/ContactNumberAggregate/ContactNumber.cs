using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

/// <summary>
/// A shopper's registered mobile contact number. <see cref="PhoneNumber"/> is always the
/// provider's canonical E.164 form (from the Twilio Lookup), never the raw caller input.
/// Belongs to exactly one shopper (<see cref="BuyerId"/> == the token username).
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

    /// <summary>The owning shopper (token username). A number belongs to whoever registered it.</summary>
    public string BuyerId { get; private set; }

    /// <summary>Canonical E.164 number as returned by the provider. Never written to logs.</summary>
    public string PhoneNumber { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; } = DateTimeOffset.UtcNow;
}
