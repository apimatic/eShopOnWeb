using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A single accepted usage report against a subscription's metered component (plan.md UC2).
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

    /// <summary>Provider-assigned usage identifier. Long because the provider allocates it from a wide range.</summary>
    public long Id { get; }

    public int Quantity { get; }

    public string? Memo { get; }

    public DateTimeOffset? RecordedAt { get; }
}
