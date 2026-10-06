using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Models;

/// <summary>
/// A subscribable plan, as exposed by the billing system.
/// </summary>
public class SubscriptionPlan
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = string.Empty;
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
}

/// <summary>
/// The state of one of the caller's subscriptions, confirmed against the
/// billing system of record.
/// </summary>
public class SubscriptionSummary
{
    public int SubscriptionId { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public DateTime? NextBillingDateUtc { get; set; }
    /// <summary>
    /// True when this call created the subscription; false when the caller was
    /// already subscribed (idempotent replay).
    /// </summary>
    public bool Created { get; set; }
}