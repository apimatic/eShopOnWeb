using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A single reported consumption of a metered resource against a subscription (UC2).
/// </summary>
public class UsageRecord
{
    public UsageRecord(long id,
        int subscriptionId,
        string componentHandle,
        decimal quantity,
        string? memo,
        DateTimeOffset? recordedAt)
    {
        Id = id;
        SubscriptionId = subscriptionId;
        ComponentHandle = componentHandle;
        Quantity = quantity;
        Memo = memo;
        RecordedAt = recordedAt;
    }

    public long Id { get; private set; }
    public int SubscriptionId { get; private set; }
    public string ComponentHandle { get; private set; }
    public decimal Quantity { get; private set; }
    public string? Memo { get; private set; }
    public DateTimeOffset? RecordedAt { get; private set; }
}
