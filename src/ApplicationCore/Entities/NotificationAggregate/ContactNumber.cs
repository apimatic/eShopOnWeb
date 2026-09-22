using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A mobile number a shopper has put on file so the shop can reach them by SMS. The stored value is
/// the provider's canonical E.164 form (not whatever the caller typed), and the number belongs to
/// the shopper who registered it — one shopper never sees, uses, or deletes another's.
/// </summary>
public class ContactNumber : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private ContactNumber() { }
#pragma warning restore CS8618

    public ContactNumber(string buyerId, string phoneNumber, string? countryCode)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(phoneNumber, nameof(phoneNumber));

        BuyerId = buyerId;
        PhoneNumber = phoneNumber;
        CountryCode = countryCode;
        RegisteredAtUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>Owner of this number — the token identity of the shopper who registered it.</summary>
    public string BuyerId { get; private set; }

    /// <summary>The provider's canonical E.164 form of the number. Sensitive; never written to logs.</summary>
    public string PhoneNumber { get; private set; }

    /// <summary>ISO country code the provider reported for the number, when available.</summary>
    public string? CountryCode { get; private set; }

    public DateTimeOffset RegisteredAtUtc { get; private set; }
}
