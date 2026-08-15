using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A shopper's subscription, as confirmed by the billing system.
/// </summary>
public class SubscriptionDto
{
    /// <summary>Numeric subscription id in the billing system.</summary>
    public int SubscriptionId { get; set; }

    /// <summary>Handle of the plan the subscription is bound to.</summary>
    public string? PlanHandle { get; set; }

    /// <summary>Name of the plan.</summary>
    public string? PlanName { get; set; }

    /// <summary>Numeric product id.</summary>
    public int? ProductId { get; set; }

    /// <summary>Recurring price in integer minor units (cents).</summary>
    public long? PriceInCents { get; set; }

    /// <summary>Recurring price in major units (e.g. dollars) for display.</summary>
    public decimal? Price { get; set; }

    /// <summary>Subscription lifecycle state (e.g. "active", "pending").</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>
    /// Date the shopper is next billed. The billing system reports this as the end of the
    /// current billing period.
    /// </summary>
    public DateTimeOffset? NextBillingDate { get; set; }

    /// <summary>End of the current billing period (same value as <see cref="NextBillingDate"/>).</summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>When the subscription is next assessed for billing, when supplied.</summary>
    public DateTimeOffset? NextAssessmentAt { get; set; }
}
