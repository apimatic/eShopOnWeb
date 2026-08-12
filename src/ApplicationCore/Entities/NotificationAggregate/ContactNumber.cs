using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A mobile number a shopper has put on file so the shop can reach them by SMS.
/// The stored <see cref="PhoneNumber"/> is always the provider's canonical E.164 form,
/// never the raw text the caller typed. A number belongs to exactly one shopper.
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

    /// <summary>The owning shopper's identity (the JWT subject / user name).</summary>
    public string BuyerId { get; private set; }

    /// <summary>Canonical E.164 number as returned by the provider's Lookup. Not written to logs.</summary>
    public string PhoneNumber { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; } = DateTimeOffset.UtcNow;
}
