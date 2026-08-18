using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

/// <summary>
/// A mobile contact number a shopper has put on file so the shop can reach them by SMS.
/// The stored value is always the messaging provider's own canonical E.164 form, never the
/// raw text the caller typed. A number belongs to exactly one shopper (<see cref="BuyerId"/>).
/// The number itself is sensitive and must never be written to logs.
/// </summary>
public class ContactNumber : BaseEntity, IAggregateRoot
{
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

    /// <summary>Identity of the shopper who owns this number (the JWT caller's name).</summary>
    public string BuyerId { get; private set; }

    /// <summary>Provider-canonical E.164 form of the number. Sensitive — never log.</summary>
    public string E164Number { get; private set; }

    public DateTimeOffset RegisteredDate { get; private set; } = DateTimeOffset.Now;
}
