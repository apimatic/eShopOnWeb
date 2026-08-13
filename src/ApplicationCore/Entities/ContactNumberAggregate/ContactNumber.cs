using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

/// <summary>
/// A mobile number a shopper has put on file so the shop can text them about their orders.
/// The stored <see cref="PhoneNumber"/> is always the provider's canonical E.164 form (never the
/// raw string the caller typed). A contact number belongs to exactly one shopper, identified by
/// <see cref="BuyerId"/>; all reads and deletes are scoped by it so one shopper can never see or
/// touch another's numbers. The number itself is PII and must never be written to logs.
/// </summary>
public class ContactNumber : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private ContactNumber() { }
#pragma warning restore CS8618

    public ContactNumber(string buyerId, string phoneNumber, string? displayFormat, string? countryCode)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(phoneNumber, nameof(phoneNumber));

        BuyerId = buyerId;
        PhoneNumber = phoneNumber;
        DisplayFormat = displayFormat;
        CountryCode = countryCode;
        RegisteredAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The owning shopper's identity (the username carried in their token).</summary>
    public string BuyerId { get; private set; }

    /// <summary>The number in the provider's canonical E.164 form. PII — never logged.</summary>
    public string PhoneNumber { get; private set; }

    /// <summary>The provider's national/presentation format, for showing back to the shopper. PII.</summary>
    public string? DisplayFormat { get; private set; }

    /// <summary>ISO country code the provider resolved for the number.</summary>
    public string? CountryCode { get; private set; }

    public DateTimeOffset RegisteredAt { get; private set; }
}
