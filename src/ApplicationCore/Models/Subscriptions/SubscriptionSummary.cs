using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Subscriptions;

/// <summary>
/// A recurring subscription as recorded in Maxio Advanced Billing.
/// </summary>
public class SubscriptionSummary
{
    public long Id { get; set; }
    public string? Reference { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public string State { get; set; } = string.Empty;
    public long? CustomerId { get; set; }
    public DateTime? ActivatedAt { get; set; }
    /// <summary>
    /// End of the current billing period, i.e. when the next regularly scheduled
    /// charge will occur (current_period_ends_at in Maxio).
    /// </summary>
    public DateTime? NextBillingAt { get; set; }
    public DateTime? CanceledAt { get; set; }
    public bool? CancelAtEndOfPeriod { get; set; }
}
