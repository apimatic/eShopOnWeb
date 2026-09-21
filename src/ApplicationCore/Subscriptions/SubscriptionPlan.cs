namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscribable plan (a Maxio product within the configured product family).
/// Handles are stable; numeric ids are not, so callers subscribe by <see cref="Handle"/>.
/// </summary>
public sealed record SubscriptionPlan
{
    /// <summary>Provider numeric id (informational only — not stable across catalog re-seeds).</summary>
    public int? Id { get; init; }

    /// <summary>Stable API handle; the value a caller passes to subscribe.</summary>
    public required string Handle { get; init; }

    /// <summary>Human-readable plan name.</summary>
    public string? Name { get; init; }

    /// <summary>Plan description.</summary>
    public string? Description { get; init; }

    /// <summary>Recurring price in integer cents.</summary>
    public long? PriceInCents { get; init; }

    /// <summary>Billing interval count (e.g. 1).</summary>
    public int? Interval { get; init; }

    /// <summary>Billing interval unit wire value (e.g. <c>month</c>, <c>day</c>).</summary>
    public string? IntervalUnit { get; init; }

    /// <summary>Whether the plan requires a stored payment method to subscribe.</summary>
    public bool? RequiresPaymentMethod { get; init; }
}
