using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

/// <summary>
/// A mobile contact number a shopper has put on file so the shop can reach them by SMS.
/// The stored <see cref="PhoneNumber"/> is always the provider's canonical E.164 form,
/// not whatever the caller originally typed.
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

    /// <summary>The username (email) of the shopper who owns this number.</summary>
    public string BuyerId { get; private set; }

    /// <summary>The canonical E.164 number as confirmed by the messaging provider.</summary>
    public string PhoneNumber { get; private set; }

    public DateTimeOffset RegisteredAt { get; private set; } = DateTimeOffset.UtcNow;
}
