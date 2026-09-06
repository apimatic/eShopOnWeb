using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public class UserSubscriptionMapping : BaseEntity, IAggregateRoot
{
    public string UserId { get; private set; } = null!;
    public int MaxioCustomerId { get; private set; }
    public DateTime CreatedAt { get; private set; }

    #pragma warning disable CS8618
    private UserSubscriptionMapping() { }

    public UserSubscriptionMapping(string userId, int maxioCustomerId)
    {
        UserId = userId;
        MaxioCustomerId = maxioCustomerId;
        CreatedAt = DateTime.UtcNow;
    }
}
