using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public class MaxioCustomerMapping : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private MaxioCustomerMapping() { }

    public MaxioCustomerMapping(string userId, int maxioCustomerId)
    {
        UserId = userId;
        MaxioCustomerId = maxioCustomerId;
    }

    public string UserId { get; private set; }
    public int MaxioCustomerId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
}
