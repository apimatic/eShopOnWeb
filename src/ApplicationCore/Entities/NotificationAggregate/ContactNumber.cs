using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A mobile number a shopper has put on file so the shop can text them. What is stored is the
/// provider's own canonical (E.164) form of the number, never whatever the caller typed.
/// Belongs to exactly one shopper (<see cref="BuyerId"/>); ownership is enforced by every query.
/// </summary>
public class ContactNumber : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private ContactNumber() { }

    public ContactNumber(string buyerId, string e164Number, string? countryCode)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(e164Number, nameof(e164Number));

        BuyerId = buyerId;
        E164Number = e164Number;
        CountryCode = countryCode;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Identity of the owning shopper (the token's name claim / username).</summary>
    public string BuyerId { get; private set; }

    /// <summary>Provider-canonical E.164 number, e.g. <c>+14155552671</c>.</summary>
    public string E164Number { get; private set; }

    /// <summary>ISO country code the provider reported for the number, if any.</summary>
    public string? CountryCode { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
