namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscription plan a shopper can enroll in. This is a provider-agnostic view of a Maxio
/// product within the configured product family; it deliberately carries no billing-SDK types.
/// </summary>
public record SubscriptionPlan
{
    /// <summary>Stable API handle of the plan (e.g. <c>eshop-pro</c>). Handles are stable across re-seeds; numeric ids are not.</summary>
    public required string Handle { get; init; }

    /// <summary>Human-friendly plan name.</summary>
    public required string Name { get; init; }

    public string? Description { get; init; }

    /// <summary>Recurring price in integer cents (e.g. 29900 for $299.00).</summary>
    public long PriceInCents { get; init; }

    /// <summary>The numeric billing interval (e.g. 1 in "every 1 month").</summary>
    public int? IntervalCount { get; init; }

    /// <summary>The billing interval unit (e.g. "month" or "day").</summary>
    public string? IntervalUnit { get; init; }

    /// <summary>Current numeric product id in the billing provider. Informational only — not stable across re-seeds.</summary>
    public int? ProductId { get; init; }
}
