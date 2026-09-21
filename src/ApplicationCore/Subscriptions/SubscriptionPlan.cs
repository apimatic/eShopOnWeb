namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscription plan a shopper can subscribe to. This is the eShopOnWeb domain view of a Maxio
/// product; it intentionally carries no billing-provider types so the abstraction stays SDK-free.
/// </summary>
public record SubscriptionPlan
{
    /// <summary>The stable API handle of the plan (e.g. <c>eshop-pro</c>). Handles are stable; ids are not.</summary>
    public required string Handle { get; init; }

    /// <summary>Human-friendly plan name.</summary>
    public required string Name { get; init; }

    /// <summary>Optional plan description.</summary>
    public string? Description { get; init; }

    /// <summary>Recurring price in integer cents (e.g. 29900 = $299.00).</summary>
    public required long PriceInCents { get; init; }

    /// <summary>ISO currency code when the provider reports one; defaults to <c>USD</c> otherwise.</summary>
    public string Currency { get; init; } = "USD";

    /// <summary>Billing interval unit as reported by the provider (e.g. <c>month</c>, <c>day</c>).</summary>
    public string? IntervalUnit { get; init; }

    /// <summary>Number of interval units per billing period (e.g. 1 month).</summary>
    public int? IntervalCount { get; init; }
}
