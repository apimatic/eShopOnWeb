using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;

/// <summary>
/// A mobile number a shopper has put on file so the shop can reach them by SMS.
/// The stored <see cref="PhoneNumber"/> is always the provider's canonical E.164 form,
/// never the raw value the caller typed.
/// </summary>
public class ContactNumber : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private ContactNumber() { }
#pragma warning restore CS8618

    public ContactNumber(string buyerId, string phoneNumber)
    {
        BuyerId = Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        PhoneNumber = Guard.Against.NullOrEmpty(phoneNumber, nameof(phoneNumber));
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The owning shopper's identity (username / email carried on the JWT).</summary>
    public string BuyerId { get; private set; }

    /// <summary>Canonical E.164 number as returned by the provider's lookup.</summary>
    public string PhoneNumber { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
