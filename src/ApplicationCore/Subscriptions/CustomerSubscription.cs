using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's subscription as recorded by the billing system of record. Provider-agnostic
/// projection returned to callers after subscribing and when listing their subscriptions.
/// </summary>
public record CustomerSubscription
{
    /// <summary>Billing-system subscription id.</summary>
    public required int Id { get; init; }

    /// <summary>Handle of the plan this subscription is for.</summary>
    public string? PlanHandle { get; init; }

    public string? PlanName { get; init; }

    /// <summary>Lifecycle state (e.g. <c>active</c>, <c>trialing</c>, <c>canceled</c>).</summary>
    public string? State { get; init; }

    /// <summary>Recurring price in minor units (cents) for this subscription.</summary>
    public long? PriceInCents { get; init; }

    /// <summary>Recurring price as a decimal amount, when a price is known.</summary>
    public decimal? Price => PriceInCents.HasValue ? PriceInCents.Value / 100m : null;

    public string? Currency { get; init; }

    /// <summary>End of the current billing period.</summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    /// <summary>Date the subscription is next assessed/billed.</summary>
    public DateTimeOffset? NextBillingDate { get; init; }

    /// <summary>The app-supplied reference stored on the subscription, when present.</summary>
    public string? Reference { get; init; }
}
