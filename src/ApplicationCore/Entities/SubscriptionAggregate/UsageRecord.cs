using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>A single reported instance of metered consumption (UC2).</summary>
public class UsageRecord
{
    public UsageRecord(long id,
        int subscriptionId,
        int componentId,
        string? componentHandle,
        decimal quantity,
        string? memo,
        DateTimeOffset createdAt)
    {
        Id = id;
        SubscriptionId = subscriptionId;
        ComponentId = componentId;
        ComponentHandle = componentHandle;
        Quantity = quantity;
        Memo = memo;
        CreatedAt = createdAt;
    }

    public long Id { get; }
    public int SubscriptionId { get; }
    public int ComponentId { get; }
    public string? ComponentHandle { get; }
    public decimal Quantity { get; }
    public string? Memo { get; }
    public DateTimeOffset CreatedAt { get; }
}
