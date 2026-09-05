using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

/// <summary>
/// The durable link between an eShop identity and its Maxio records.
/// Maxio remains the billing system of record; this entity is only an integration index.
/// </summary>
public class SubscriptionMapping : BaseEntity, IAggregateRoot
{
    private SubscriptionMapping()
    {
    }

    public SubscriptionMapping(string userId, int maxioCustomerId, int maxioSubscriptionId, string planHandle)
    {
        UserId = userId;
        MaxioCustomerId = maxioCustomerId;
        MaxioSubscriptionId = maxioSubscriptionId;
        PlanHandle = planHandle;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public string UserId { get; private set; } = string.Empty;
    public int MaxioCustomerId { get; private set; }
    public int MaxioSubscriptionId { get; private set; }
    public string PlanHandle { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void Update(int maxioCustomerId, int maxioSubscriptionId, string planHandle)
    {
        MaxioCustomerId = maxioCustomerId;
        MaxioSubscriptionId = maxioSubscriptionId;
        PlanHandle = planHandle;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
