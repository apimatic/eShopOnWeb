using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

/// <summary>
/// A shopper's registered mobile number, stored in the provider's canonical (E.164) form.
/// </summary>
public class ContactNumber : BaseEntity, IAggregateRoot
{
    public string BuyerId { get; private set; }
    public string PhoneNumber { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    #pragma warning disable CS8618 // Required by Entity Framework
    private ContactNumber() {}

    public ContactNumber(string buyerId, string phoneNumber)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(phoneNumber, nameof(phoneNumber));

        BuyerId = buyerId;
        PhoneNumber = phoneNumber;
    }
}
