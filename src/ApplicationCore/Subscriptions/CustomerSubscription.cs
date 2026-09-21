using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's enrollment in a plan, as reported back by the billing provider. Provider-agnostic;
/// carries no billing-SDK types.
/// </summary>
public record CustomerSubscription
{
    /// <summary>The subscription id in the billing provider.</summary>
    public required int Id { get; init; }

    /// <summary>Handle of the subscribed plan (product), when known.</summary>
    public string? PlanHandle { get; init; }

    /// <summary>Name of the subscribed plan (product), when known.</summary>
    public string? PlanName { get; init; }

    /// <summary>The recurring product price in integer cents currently subscribed to.</summary>
    public long? PriceInCents { get; init; }

    /// <summary>The subscription state (e.g. "active", "trialing", "canceled").</summary>
    public required string State { get; init; }

    /// <summary>When the next billing attempt is scheduled (falls back to the current period end).</summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    /// <summary>End of the current billing period.</summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }
}
