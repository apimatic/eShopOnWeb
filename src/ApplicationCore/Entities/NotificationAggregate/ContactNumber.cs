using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A mobile contact number a shopper has put on file so the shop can reach them by SMS.
/// The stored <see cref="PhoneNumber"/> is always the provider's canonical E.164 form, never
/// the raw value the caller typed. A contact number belongs to exactly one shopper.
/// </summary>
public class ContactNumber : BaseEntity, IAggregateRoot
{
    /// <summary>The owning shopper's identity (the username carried on the JWT / Order.BuyerId).</summary>
    public string BuyerId { get; private set; }

    /// <summary>The provider's canonical E.164 representation of the number.</summary>
    public string PhoneNumber { get; private set; }

    public DateTimeOffset RegisteredDate { get; private set; }

#pragma warning disable CS8618 // Required by Entity Framework
    private ContactNumber() { }
#pragma warning restore CS8618

    public ContactNumber(string buyerId, string canonicalE164PhoneNumber)
    {
        BuyerId = Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        PhoneNumber = Guard.Against.NullOrEmpty(canonicalE164PhoneNumber, nameof(canonicalE164PhoneNumber));
        RegisteredDate = DateTimeOffset.UtcNow;
    }
}
