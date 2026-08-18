using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A mobile contact number a shopper has put on file so the shop can text them.
/// The stored value is always the provider's canonical E.164 form of the number,
/// never the raw text the caller typed. A number belongs to the shopper that
/// registered it and is never written to logs.
/// </summary>
public class ContactNumber : BaseEntity, IAggregateRoot
{
    public string OwnerId { get; private set; }
    public string E164Number { get; private set; }
    public DateTimeOffset RegisteredAt { get; private set; } = DateTimeOffset.UtcNow;

#pragma warning disable CS8618 // Required by Entity Framework
    private ContactNumber() { }
#pragma warning restore CS8618

    public ContactNumber(string ownerId, string e164Number)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(e164Number, nameof(e164Number));

        OwnerId = ownerId;
        E164Number = e164Number;
    }
}
