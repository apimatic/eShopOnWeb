using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A single metered-usage event accepted by the billing provider (UC2).
/// </summary>
public sealed record UsageRecord
{
    public required long Id { get; init; }

    public required int SubscriptionId { get; init; }

    public required int ComponentId { get; init; }

    public string? ComponentHandle { get; init; }

    /// <summary>The number of units recorded by this event.</summary>
    public required decimal Quantity { get; init; }

    public string? Memo { get; init; }

    public DateTimeOffset? RecordedAt { get; init; }
}
