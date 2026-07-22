using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A subscription as reported by the billing provider, normalized by the billing client.
/// </summary>
public class BillingSubscription
{
    public int Id { get; init; }

    /// <summary>The provider's lifecycle state, e.g. "active", "on_hold", "canceled".</summary>
    public string State { get; init; } = string.Empty;
    public int CustomerId { get; init; }
    public string? CustomerReference { get; init; }
    public int ProductId { get; init; }
    public string? ProductHandle { get; init; }
    public string? ProductName { get; init; }

    /// <summary>The plan price in major currency units (e.g. 299.00 — not cents).</summary>
    public decimal ProductPrice { get; init; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
    public DateTimeOffset? NextAssessmentAt { get; init; }
    public bool CancelAtEndOfPeriod { get; init; }
    public DateTimeOffset? DelayedCancelAt { get; init; }

    /// <summary>The outstanding balance in major currency units.</summary>
    public decimal Balance { get; init; }
}
