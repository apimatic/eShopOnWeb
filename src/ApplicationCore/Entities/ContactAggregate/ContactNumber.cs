using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;

public class ContactNumber : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618
    private ContactNumber() { }
    #pragma warning restore CS8618

    public ContactNumber(string buyerId, string e164Number)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(e164Number, nameof(e164Number));

        BuyerId = buyerId;
        Number = e164Number;
        CreatedUtc = DateTimeOffset.UtcNow;
    }

    public string BuyerId { get; private set; }
    public string Number { get; private set; }
    public DateTimeOffset CreatedUtc { get; private set; }
}
