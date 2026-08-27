using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public class RegisteredContactNumber : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618
    private RegisteredContactNumber() { }
#pragma warning restore CS8618

    public RegisteredContactNumber(string buyerId, string canonicalNumber, DateTimeOffset createdAt)
    {
        BuyerId = buyerId;
        CanonicalNumber = canonicalNumber;
        CreatedAt = createdAt;
    }

    public string BuyerId { get; private set; }
    public string CanonicalNumber { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? RemovedAt { get; private set; }
    public bool IsActive => RemovedAt is null;

    public bool Remove(DateTimeOffset removedAt)
    {
        if (RemovedAt is not null)
        {
            return false;
        }

        RemovedAt = removedAt;
        return true;
    }
}
