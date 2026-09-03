namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A recurring subscription plan a shopper can subscribe to. This is a plain domain projection
/// of a Maxio product within the configured product family — no billing-SDK type leaks past this
/// boundary.
/// </summary>
public record SubscriptionPlan
{
    /// <summary>Stable API handle of the plan (used to subscribe). Never the numeric id.</summary>
    public required string Handle { get; init; }

    public string? Name { get; init; }

    public string? Description { get; init; }

    /// <summary>Recurring price in integer cents.</summary>
    public long? PriceInCents { get; init; }

    /// <summary>Billing interval count (e.g. 1).</summary>
    public int? Interval { get; init; }

    /// <summary>Billing interval unit wire value (e.g. "month", "day").</summary>
    public string? IntervalUnit { get; init; }
}
