using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A single accepted report of metered consumption against a subscription's component.
/// </summary>
public class UsageRecord
{
    public UsageRecord(long id,
        int subscriptionId,
        int componentId,
        string componentHandle,
        decimal quantity,
        string? memo,
        DateTimeOffset? recordedAt)
    {
        Guard.Against.NullOrEmpty(componentHandle, nameof(componentHandle));

        Id = id;
        SubscriptionId = subscriptionId;
        ComponentId = componentId;
        ComponentHandle = componentHandle;
        Quantity = quantity;
        Memo = memo;
        RecordedAt = recordedAt;
    }

    public long Id { get; private set; }

    public int SubscriptionId { get; private set; }

    public int ComponentId { get; private set; }

    public string ComponentHandle { get; private set; }

    /// <summary>Units consumed. This is a raw count, not a money amount.</summary>
    public decimal Quantity { get; private set; }

    public string? Memo { get; private set; }

    public DateTimeOffset? RecordedAt { get; private set; }
}
