using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

public class ContactNumber : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618
    private ContactNumber() { }
#pragma warning restore CS8618

    public ContactNumber(string buyerId, string phoneNumberE164, string? nationalFormat)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(phoneNumberE164, nameof(phoneNumberE164));

        BuyerId = buyerId;
        PhoneNumberE164 = phoneNumberE164;
        NationalFormat = nationalFormat;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string BuyerId { get; private set; }
    public string PhoneNumberE164 { get; private set; }
    public string? NationalFormat { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
