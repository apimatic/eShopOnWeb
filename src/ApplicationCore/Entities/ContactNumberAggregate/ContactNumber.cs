using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

/// <summary>
/// A mobile number a shopper has put on file so the shop can text them as their orders
/// progress. What is stored is the provider's own canonical E.164 form of the number,
/// never the raw string the caller typed.
/// </summary>
public class ContactNumber : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private ContactNumber() { }
#pragma warning restore CS8618

    public ContactNumber(string ownerId, string e164Number)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(e164Number, nameof(e164Number));

        OwnerId = ownerId;
        E164Number = e164Number;
        RegisteredAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The shopper who registered the number (their identity, i.e. username from the token).</summary>
    public string OwnerId { get; private set; }

    /// <summary>The provider's canonical E.164 representation of the number.</summary>
    public string E164Number { get; private set; }

    public DateTimeOffset RegisteredAt { get; private set; }
}
