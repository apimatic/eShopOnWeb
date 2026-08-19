using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;

/// <summary>
/// A mobile number a shopper has put on file so the shop can text them about their orders.
/// The stored value is always the messaging provider's canonical E.164 form of the number,
/// never the raw text the caller typed.
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
        CreatedDate = DateTimeOffset.UtcNow;
    }

    /// <summary>Identity of the shopper who registered this number (their user name / login).</summary>
    public string OwnerId { get; private set; }

    /// <summary>The provider's canonical E.164 representation of the number.</summary>
    public string E164Number { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; }
}
