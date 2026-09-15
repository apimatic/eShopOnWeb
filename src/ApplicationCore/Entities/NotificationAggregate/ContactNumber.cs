using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public sealed class ContactNumber : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618
    private ContactNumber() { }
#pragma warning restore CS8618

    public ContactNumber(string buyerId, string canonicalNumber, DateTimeOffset registeredAt)
    {
        BuyerId = buyerId;
        CanonicalNumber = canonicalNumber;
        RegisteredAt = registeredAt;
    }

    public string BuyerId { get; private set; }
    public string CanonicalNumber { get; private set; }
    public DateTimeOffset RegisteredAt { get; private set; }
}
