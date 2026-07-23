using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A single reported consumption of a metered component, which accrues to the next renewal invoice.
/// </summary>
public class UsageRecord
{
    public UsageRecord(long id, int subscriptionId, int componentId, string? componentHandle,
        decimal quantity, string? memo, DateTimeOffset? createdAt)
    {
        Id = id;
        SubscriptionId = subscriptionId;
        ComponentId = componentId;
        ComponentHandle = componentHandle;
        Quantity = quantity;
        Memo = memo;
        CreatedAt = createdAt;
    }

    public long Id { get; private set; }

    public int SubscriptionId { get; private set; }

    public int ComponentId { get; private set; }

    public string? ComponentHandle { get; private set; }

    public decimal Quantity { get; private set; }

    public string? Memo { get; private set; }

    public DateTimeOffset? CreatedAt { get; private set; }
}
