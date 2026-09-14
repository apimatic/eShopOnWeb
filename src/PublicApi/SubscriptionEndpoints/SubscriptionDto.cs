using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription as reflected by the Maxio billing system of record.
/// </summary>
public class SubscriptionDto
{
    /// <summary>Maxio numeric subscription id.</summary>
    public int? Id { get; set; }

    /// <summary>Maxio subscription reference supplied by the app at creation time.</summary>
    public string? Reference { get; set; }

    /// <summary>Handle of the subscribed plan.</summary>
    public string? PlanHandle { get; set; }

    /// <summary>Display name of the subscribed plan.</summary>
    public string? PlanName { get; set; }

    /// <summary>Current recurring charge for one interval, in the subscription currency.</summary>
    public decimal? Price { get; set; }

    /// <summary>ISO currency code (e.g. "USD").</summary>
    public string? Currency { get; set; }

    /// <summary>Number of interval units per billing cycle.</summary>
    public int? Interval { get; set; }

    /// <summary>Billing interval unit: "month", "day", etc.</summary>
    public string? IntervalUnit { get; set; }

    /// <summary>Subscription lifecycle state (wire value, e.g. "active").</summary>
    public string? State { get; set; }

    /// <summary>End of the current billing period, i.e. the next billing date for a healthy subscription.</summary>
    public DateTimeOffset? NextBillingDate { get; set; }
}
