using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A single quantity of metered consumption recorded against a subscription's component.
/// </summary>
public class UsageRecord
{
    public UsageRecord(long id, int quantity, string? memo, DateTimeOffset? recordedAt)
    {
        Id = id;
        Quantity = quantity;
        Memo = memo;
        RecordedAt = recordedAt;
    }

    /// <summary>The provider-assigned usage identifier.</summary>
    public long Id { get; }

    /// <summary>Units consumed.</summary>
    public int Quantity { get; }

    public string? Memo { get; }

    public DateTimeOffset? RecordedAt { get; }
}
