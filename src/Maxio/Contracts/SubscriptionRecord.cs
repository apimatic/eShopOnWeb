using System;

namespace Microsoft.eShopWeb.Maxio.Contracts;

/// <summary>
/// A subscription on the Maxio site, projected for API consumers.
/// </summary>
public sealed class SubscriptionRecord
{
    public long Id { get; set; }

    /// <summary>Maxio subscription state (for example: active, canceled, past_due, on_hold).</summary>
    public string State { get; set; } = string.Empty;

    public string PlanHandle { get; set; } = string.Empty;

    public string PlanName { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public int Interval { get; set; }

    public string IntervalUnit { get; set; } = string.Empty;

    public string Currency { get; set; } = string.Empty;

    public long CustomerId { get; set; }

    public string CustomerReference { get; set; } = string.Empty;

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    /// <summary>End of the current billing period (the next renewal/billing date).</summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>When Maxio next assesses the subscription.</summary>
    public DateTimeOffset? NextAssessmentAt { get; set; }

    public decimal Balance { get; set; }
}
