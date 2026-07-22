using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A single metered-usage entry recorded against a subscription's metered component.
/// </summary>
public class UsageRecord
{
    public UsageRecord(long id, int subscriptionId, decimal quantity)
    {
        Id = id;
        SubscriptionId = subscriptionId;
        Quantity = quantity;
    }

    public long Id { get; }

    public int SubscriptionId { get; }

    /// <summary>The number of units consumed. Negative quantities deduct previously recorded usage.</summary>
    public decimal Quantity { get; }

    public string? Memo { get; init; }

    public DateTimeOffset? RecordedAt { get; init; }

    public int? ComponentId { get; init; }

    public string? ComponentHandle { get; init; }
}
