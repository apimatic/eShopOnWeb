using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A mobile number a shopper has put on file so the shop can text them. What is stored is the provider's
/// canonical E.164 form of the number, not whatever the caller typed. Belongs to exactly one shopper.
/// The number value is never written to logs.
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
        CreatedDate = DateTimeOffset.UtcNow;
    }

    /// <summary>The owning shopper (the username carried on the JWT).</summary>
    public string BuyerId { get; private set; }

    /// <summary>The provider's canonical E.164 form of the number. Never logged.</summary>
    public string E164Number { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; }
}
