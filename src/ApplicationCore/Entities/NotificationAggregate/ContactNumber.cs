using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A mobile number a shopper has put on file so the shop can text them. The stored
/// <see cref="Value"/> is always the provider's canonical E.164 form (never the raw caller input),
/// and the number belongs to the shopper who registered it (<see cref="OwnerId"/>).
/// </summary>
public class ContactNumber : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private ContactNumber() { }

    public ContactNumber(string ownerId, string value)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(value, nameof(value));

        OwnerId = ownerId;
        Value = value;
        RegisteredAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Identity of the shopper who owns this number (the buyer id / token name claim).</summary>
    public string OwnerId { get; private set; }

    /// <summary>Canonical E.164 number as returned by the provider's lookup.</summary>
    public string Value { get; private set; }

    public DateTimeOffset RegisteredAt { get; private set; }
}
