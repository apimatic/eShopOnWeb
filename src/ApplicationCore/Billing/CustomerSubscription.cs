using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// A shopper's enrollment in a <see cref="SubscriptionPlan"/>, as reported by the
/// billing system of record (Maxio). Billing-system-agnostic projection.
/// </summary>
public class CustomerSubscription
{
    /// <summary>Billing-system identifier of the subscription (Maxio subscription id).</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Lifecycle state as reported by Maxio (e.g. "active", "trialing").</summary>
    public string State { get; init; } = string.Empty;

    /// <summary>Handle of the plan the shopper is enrolled in.</summary>
    public string PlanHandle { get; init; } = string.Empty;

    public string PlanName { get; init; } = string.Empty;

    /// <summary>Recurring price in cents at the time of enrollment.</summary>
    public long PriceInCents { get; init; }

    public decimal Price => PriceInCents / 100m;

    public int Interval { get; init; }

    public string? IntervalUnit { get; init; }

    /// <summary>When the next charge/assessment is due, if scheduled.</summary>
    public DateTimeOffset? NextBillingDate { get; init; }

    /// <summary>When the current billing period ends, if known.</summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    /// <summary>When the subscription was created in Maxio.</summary>
    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>Stable external reference tying the subscription to the eShopOnWeb user.</summary>
    public string CustomerReference { get; init; } = string.Empty;
}
