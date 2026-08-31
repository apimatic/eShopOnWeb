using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A shopper's registered mobile number, stored in the provider's canonical (E.164) form.
/// </summary>
public class ContactNumber : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private ContactNumber() { }

    public ContactNumber(string buyerId, string phoneNumber, string? nationalFormat)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(phoneNumber, nameof(phoneNumber));

        BuyerId = buyerId;
        PhoneNumber = phoneNumber;
        NationalFormat = nationalFormat;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string BuyerId { get; private set; }

    // Provider-canonical E.164 form, e.g. +14159929960
    public string PhoneNumber { get; private set; }
    public string? NationalFormat { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
