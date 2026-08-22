using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

public class ContactNumber : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618
    private ContactNumber() { }
    #pragma warning restore CS8618

    public ContactNumber(string buyerId, string canonicalPhoneNumber)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(canonicalPhoneNumber, nameof(canonicalPhoneNumber));

        BuyerId = buyerId;
        PhoneNumber = canonicalPhoneNumber;
        CreatedUtc = DateTimeOffset.UtcNow;
    }

    public string BuyerId { get; private set; }
    public string PhoneNumber { get; private set; }
    public DateTimeOffset CreatedUtc { get; private set; }
}
