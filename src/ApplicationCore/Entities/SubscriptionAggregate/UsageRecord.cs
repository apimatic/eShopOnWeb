using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A single accepted usage report against a subscription's metered component (UC2).
/// </summary>
public class UsageRecord
{
    public UsageRecord(long id,
        decimal quantity,
        string? memo,
        int? componentId,
        string? componentHandle,
        int? subscriptionId,
        DateTimeOffset? createdAt)
    {
        Id = id;
        Quantity = quantity;
        Memo = memo;
        ComponentId = componentId;
        ComponentHandle = componentHandle;
        SubscriptionId = subscriptionId;
        CreatedAt = createdAt;
    }

    public long Id { get; }

    /// <summary>The number of units the provider accepted for this report.</summary>
    public decimal Quantity { get; }

    public string? Memo { get; }

    public int? ComponentId { get; }

    public string? ComponentHandle { get; }

    public int? SubscriptionId { get; }

    public DateTimeOffset? CreatedAt { get; }
}
