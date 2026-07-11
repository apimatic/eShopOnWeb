using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>A single unit-of-consumption record billed against a metered component (UC2).</summary>
public class UsageRecord
{
    public UsageRecord(long id, string componentHandle, double quantity, string? memo, DateTimeOffset? recordedAt)
    {
        Id = id;
        ComponentHandle = componentHandle;
        Quantity = quantity;
        Memo = memo;
        RecordedAt = recordedAt;
    }

    public long Id { get; }
    public string ComponentHandle { get; }
    public double Quantity { get; }
    public string? Memo { get; }
    public DateTimeOffset? RecordedAt { get; }
}
