using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

/// <summary>
/// A shopper's mobile contact number, validated and normalised to E.164 by the
/// messaging provider before it is stored.
/// </summary>
public class ContactNumber : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private ContactNumber() {}

    public ContactNumber(string buyerId, string phoneNumber, string? nationalFormat)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(phoneNumber, nameof(phoneNumber));

        BuyerId = buyerId;
        PhoneNumber = phoneNumber;
        NationalFormat = nationalFormat;
    }

    public string BuyerId { get; private set; }

    /// <summary>The provider's canonical E.164 form of the number.</summary>
    public string PhoneNumber { get; private set; }

    public string? NationalFormat { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
}
