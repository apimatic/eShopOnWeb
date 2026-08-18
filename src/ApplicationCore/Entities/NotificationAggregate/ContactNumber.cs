using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A mobile number a shopper has put on file so the shop can reach them. The stored value is the
/// provider's canonical (E.164) form of the number, and the number belongs to the shopper who
/// registered it.
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
        RegisteredAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The owning shopper (their username / buyer id from the token).</summary>
    public string BuyerId { get; private set; }

    /// <summary>Canonical E.164 number as returned by the provider's lookup.</summary>
    public string PhoneNumber { get; private set; }

    public DateTimeOffset RegisteredAt { get; private set; }
}
