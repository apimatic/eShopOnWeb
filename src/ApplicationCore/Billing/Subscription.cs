using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// A recurring subscription as tracked by the billing provider.
/// </summary>
public class Subscription
{
    /// <summary>
    /// Numeric id assigned by the billing provider.
    /// </summary>
    public int Id { get; init; }

    /// <summary>
    /// App-supplied unique reference assigned when the subscription was created. Stable across provider re-seeds.
    /// </summary>
    public string Reference { get; init; } = string.Empty;

    /// <summary>
    /// Subscription lifecycle state as reported by the billing provider (e.g. "active", "trialing", "canceled").
    /// </summary>
    public string State { get; init; } = string.Empty;

    public int ProductId { get; init; }

    public string ProductHandle { get; init; } = string.Empty;

    public string ProductName { get; init; } = string.Empty;

    /// <summary>
    /// Amount charged per billing period in <see cref="Currency"/> (major units).
    /// </summary>
    public decimal Price { get; init; }

    public string Currency { get; init; } = string.Empty;

    public int Interval { get; init; }

    public string IntervalUnit { get; init; } = string.Empty;

    /// <summary>
    /// How the subscription is collected. Subscriptions created by this integration are
    /// "remittance" (invoice-based), which means no card capture or 3-DS is required.
    /// </summary>
    public string PaymentCollectionMethod { get; init; } = string.Empty;

    public int CustomerId { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? ActivatedAt { get; init; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    /// <summary>
    /// Next scheduled billing/assessment moment reported by the billing provider.
    /// </summary>
    public DateTimeOffset? NextBillingDate { get; init; }
}
