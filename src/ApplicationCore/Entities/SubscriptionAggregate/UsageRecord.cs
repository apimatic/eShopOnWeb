using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A single metered-usage event accepted by the billing provider (plan.md UC2).
/// </summary>
public sealed record UsageRecord
{
    /// <summary>Provider-assigned usage id.</summary>
    public long? Id { get; init; }

    public required int SubscriptionId { get; init; }

    public string? ComponentHandle { get; init; }

    /// <summary>The number of units recorded.</summary>
    public required decimal Quantity { get; init; }

    public string? Memo { get; init; }

    public DateTimeOffset? RecordedAt { get; init; }
}
