using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A mobile number a shopper has put on file so the shop can reach them by text.
/// The number stored here is always the provider's canonical E.164 form, never the raw
/// string the caller typed. A contact number belongs to exactly one shopper (<see cref="BuyerId"/>).
/// </summary>
public class ContactNumber : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private ContactNumber() { }
#pragma warning restore CS8618

    public ContactNumber(string buyerId, string phoneNumber, string? nationalFormat)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(phoneNumber, nameof(phoneNumber));

        BuyerId = buyerId;
        PhoneNumber = phoneNumber;
        NationalFormat = nationalFormat;
        RegisteredAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The identity (username/email) of the shopper who owns this number.</summary>
    public string BuyerId { get; private set; }

    /// <summary>The canonical E.164 destination, as validated and normalised by the provider.</summary>
    public string PhoneNumber { get; private set; }

    /// <summary>Presentation-only national format for display back to the shopper.</summary>
    public string? NationalFormat { get; private set; }

    public DateTimeOffset RegisteredAt { get; private set; }
}
