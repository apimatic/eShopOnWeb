using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

/// <summary>
/// A mobile number a shopper has put on file so the shop can text them about their orders.
/// The stored <see cref="E164Number"/> is always the provider's own canonical form of the number,
/// never the raw string the caller typed. A contact number belongs to exactly one shopper
/// (<see cref="BuyerId"/>); no other shopper may see, use, or delete it.
/// </summary>
public class ContactNumber : BaseEntity, IAggregateRoot
{
    public string BuyerId { get; private set; }

    /// <summary>The number in the provider's canonical E.164 form (e.g. "+14165551234").</summary>
    public string E164Number { get; private set; }

    public DateTimeOffset RegisteredAt { get; private set; } = DateTimeOffset.UtcNow;

#pragma warning disable CS8618 // Required by Entity Framework
    private ContactNumber() { }
#pragma warning restore CS8618

    public ContactNumber(string buyerId, string e164Number)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(e164Number, nameof(e164Number));

        BuyerId = buyerId;
        E164Number = e164Number;
    }
}
