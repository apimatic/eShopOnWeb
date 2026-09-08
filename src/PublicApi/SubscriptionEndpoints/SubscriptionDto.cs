using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// The state of the caller's subscription, confirmed back from Maxio Advanced Billing.
/// </summary>
public class SubscriptionDto
{
    /// <summary>
    /// Subscription id in Maxio Advanced Billing (the billing system of record).
    /// </summary>
    public long Id { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;

    /// <summary>
    /// Recurring price, e.g. 299.00.
    /// </summary>
    public decimal Price { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>
    /// The next billing date (Maxio's next assessment).
    /// </summary>
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
}
