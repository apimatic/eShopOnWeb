using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models;

/// <summary>
/// A shopper's subscription as recorded in Maxio Advanced Billing (the billing system of record).
/// </summary>
public class CustomerSubscription
{
    public long SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public string PaymentCollectionMethod { get; set; } = string.Empty;
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>When the next billing/assessment occurs (next_assessment_at, falling back to current_period_ends_at).</summary>
    public DateTimeOffset? NextBillingAt { get; set; }
}
