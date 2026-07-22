using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// One accepted report of metered consumption against a subscription's component.
/// </summary>
public class UsageRecord
{
    public UsageRecord(long providerUsageId, int subscriptionId, int componentId,
        string? componentHandle, decimal quantity, string? memo, DateTimeOffset? createdAt)
    {
        ProviderUsageId = providerUsageId;
        SubscriptionId = subscriptionId;
        ComponentId = componentId;
        ComponentHandle = componentHandle;
        Quantity = quantity;
        Memo = memo;
        CreatedAt = createdAt;
    }

    public long ProviderUsageId { get; }

    public int SubscriptionId { get; }

    public int ComponentId { get; }

    public string? ComponentHandle { get; }

    public decimal Quantity { get; }

    public string? Memo { get; }

    public DateTimeOffset? CreatedAt { get; }
}
