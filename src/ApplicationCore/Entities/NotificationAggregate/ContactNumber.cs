using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A mobile number a shopper has put on file so the shop can reach them by SMS. The stored
/// <see cref="PhoneNumber"/> is always the provider's canonical E.164 form, not whatever the
/// caller typed. A number belongs to exactly one shopper (<see cref="OwnerId"/>).
/// </summary>
public class ContactNumber : BaseEntity, IAggregateRoot
{
    public string OwnerId { get; private set; }

    /// <summary>Canonical E.164 form returned by the provider's lookup. This is the value used
    /// as the destination for every message and is never written to logs.</summary>
    public string PhoneNumber { get; private set; }

    /// <summary>Human-readable national format for display back to the shopper.</summary>
    public string? NationalFormat { get; private set; }

    /// <summary>ISO 3166-1 alpha-2 country of the number, when the provider reports it.</summary>
    public string? CountryCode { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; } = DateTimeOffset.UtcNow;

#pragma warning disable CS8618 // Required by Entity Framework
    private ContactNumber() { }
#pragma warning restore CS8618

    public ContactNumber(string ownerId, string phoneNumber, string? nationalFormat, string? countryCode)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(phoneNumber, nameof(phoneNumber));

        OwnerId = ownerId;
        PhoneNumber = phoneNumber;
        NationalFormat = nationalFormat;
        CountryCode = countryCode;
    }
}
