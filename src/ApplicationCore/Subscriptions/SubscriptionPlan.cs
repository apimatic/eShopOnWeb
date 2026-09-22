namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A recurring-subscription plan a shopper can subscribe to, projected from the billing
/// provider into plain, provider-agnostic terms.
/// </summary>
public record SubscriptionPlan
{
    /// <summary>The stable API handle of the plan (used to subscribe).</summary>
    public required string Handle { get; init; }

    public string? Name { get; init; }

    public string? Description { get; init; }

    /// <summary>The recurring price in the smallest currency unit (e.g. cents).</summary>
    public long PriceInCents { get; init; }

    /// <summary>The numeric billing interval (e.g. 1). Coupled with <see cref="IntervalUnit"/>.</summary>
    public int? Interval { get; init; }

    /// <summary>The billing interval unit (e.g. "month", "day").</summary>
    public string? IntervalUnit { get; init; }
}
