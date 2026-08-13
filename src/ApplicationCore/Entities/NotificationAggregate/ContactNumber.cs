using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A mobile number a shopper has put on file so the shop can reach them by SMS. The stored value is
/// the provider's own canonical (E.164) form of the number, not whatever the caller typed.
/// A contact number belongs to exactly one shopper (<see cref="BuyerId"/>).
/// </summary>
public class ContactNumber : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private ContactNumber() { }

    public ContactNumber(string buyerId, string e164Number)
    {
        BuyerId = Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        E164Number = Guard.Against.NullOrEmpty(e164Number, nameof(e164Number));
    }

    /// <summary>Owning shopper (the token's identity).</summary>
    public string BuyerId { get; private set; }

    /// <summary>Provider's canonical E.164 form of the number. Never written to logs.</summary>
    public string E164Number { get; private set; }

    public DateTimeOffset RegisteredAt { get; private set; } = DateTimeOffset.UtcNow;
}
