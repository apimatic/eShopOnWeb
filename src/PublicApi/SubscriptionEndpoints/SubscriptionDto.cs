using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A shopper's enrollment in a plan, as reported by the billing system of record.
/// </summary>
public class SubscriptionDto
{
    /// <summary>Provider-assigned subscription id.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Lifecycle bucket: Active, Pending, ProblemState, Ended, or Unknown. Use this for access
    /// decisions and <see cref="ProviderState"/> for support and reconciliation.
    /// </summary>
    public string State { get; set; } = string.Empty;

    /// <summary>Verbatim provider state, e.g. "active", "trialing", "past_due".</summary>
    public string ProviderState { get; set; } = string.Empty;

    public string PlanHandle { get; set; } = string.Empty;

    public string PlanName { get; set; } = string.Empty;

    /// <summary>The recurring amount actually being charged for this subscription.</summary>
    public decimal Price { get; set; }

    public string Currency { get; set; } = string.Empty;

    public int Interval { get; set; }

    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>Ready-to-render summary of the recurring charge, e.g. "$299.00 per month".</summary>
    public string BillingPeriod { get; set; } = string.Empty;

    /// <summary>When the next recurring charge is expected. Null once the subscription has ended.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    public DateTimeOffset? CanceledAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Provider-assigned customer id the subscription belongs to.</summary>
    public string CustomerId { get; set; } = string.Empty;

    /// <summary>
    /// The reference eShopOnWeb stores on the provider customer record. This is the join between
    /// the storefront user and the billing customer, and is what makes signup idempotent.
    /// </summary>
    public string CustomerReference { get; set; } = string.Empty;

    /// <summary>The reference eShopOnWeb stores on the subscription itself.</summary>
    public string? Reference { get; set; }
}
