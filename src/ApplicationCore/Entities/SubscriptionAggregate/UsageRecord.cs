using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A single accepted usage report against a metered component.
/// </summary>
public class UsageRecord
{
    public UsageRecord(long id, int subscriptionId, int componentId, decimal quantity, string? memo, DateTimeOffset? recordedAt)
    {
        Id = id;
        SubscriptionId = subscriptionId;
        ComponentId = componentId;
        Quantity = quantity;
        Memo = memo;
        RecordedAt = recordedAt;
    }

    public long Id { get; }

    public int SubscriptionId { get; }

    public int ComponentId { get; }

    public decimal Quantity { get; }

    public string? Memo { get; }

    public DateTimeOffset? RecordedAt { get; }
}
