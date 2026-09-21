using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A mobile number a shopper has put on file so the shop can reach them by SMS. The stored value is
/// the provider's canonical E.164 form (never whatever the caller typed), and it belongs to exactly
/// one shopper — no other shopper may see, use, or delete it. The number itself is sensitive and is
/// never written to logs.
/// </summary>
public class ContactNumber : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private ContactNumber() { }

    public ContactNumber(string ownerId, string e164Number)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(e164Number, nameof(e164Number));

        OwnerId = ownerId;
        E164Number = e164Number;
        RegisteredAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Identity of the shopper who registered this number (JWT name / order BuyerId).</summary>
    public string OwnerId { get; private set; }

    /// <summary>Provider-canonical E.164 destination. Sensitive — never logged.</summary>
    public string E164Number { get; private set; }

    public DateTimeOffset RegisteredAt { get; private set; }
}
