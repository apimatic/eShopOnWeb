using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

public class ContactNumber : BaseEntity, IAggregateRoot
{
    public string BuyerId { get; private set; }
    public string PhoneNumber { get; private set; }
    public DateTimeOffset DateCreated { get; private set; } = DateTimeOffset.UtcNow;

#pragma warning disable CS8618
    private ContactNumber() { }
#pragma warning restore CS8618

    public ContactNumber(string buyerId, string phoneNumber)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(phoneNumber, nameof(phoneNumber));

        BuyerId = buyerId;
        PhoneNumber = phoneNumber;
        DateCreated = DateTimeOffset.UtcNow;
    }
}
