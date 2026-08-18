using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A mobile number a shopper has put on file so the shop can reach them by SMS.
/// The value stored is always the messaging provider's canonical E.164 form of the
/// number, never the raw text the caller typed. A number belongs to exactly one shopper.
/// </summary>
public class ContactNumber : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private ContactNumber() { }
    #pragma warning restore CS8618

    public ContactNumber(string buyerId, string phoneNumberE164)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(phoneNumberE164, nameof(phoneNumberE164));

        BuyerId = buyerId;
        PhoneNumber = phoneNumberE164;
        RegisteredAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Identity of the shopper who owns this number (the token's name claim).</summary>
    public string BuyerId { get; private set; }

    /// <summary>The provider's canonical E.164 representation of the number.</summary>
    public string PhoneNumber { get; private set; }

    public DateTimeOffset RegisteredAt { get; private set; }
}
