using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A Maxio subscription as seen by the shopper that owns it. Confirms the plan, the price,
/// the current state and the next billing date.
/// </summary>
public class SubscriptionDto
{
    /// <summary>Maxio subscription id.</summary>
    public long Id { get; set; }

    /// <summary>Maxio subscription state (e.g. "active", "trialing", "past_due", "canceled").</summary>
    public string? State { get; set; }

    /// <summary>Handle of the subscribed plan.</summary>
    public string? PlanHandle { get; set; }

    /// <summary>Display name of the subscribed plan.</summary>
    public string? PlanName { get; set; }

    /// <summary>Price per billing cycle (decimal dollars), taken from the subscribed plan.</summary>
    public decimal Price { get; set; }

    /// <summary>Billing interval length (e.g. 1).</summary>
    public int Interval { get; set; }

    /// <summary>Billing interval unit (e.g. "month").</summary>
    public string? IntervalUnit { get; set; }

    /// <summary>Site currency code (e.g. "USD").</summary>
    public string? Currency { get; set; }

    /// <summary>When the current billing period started.</summary>
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    /// <summary>When the current billing period ends - the next billing/renewal date.</summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>When Maxio next assesses the subscription.</summary>
    public DateTimeOffset? NextAssessmentAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    public DateTimeOffset? CanceledAt { get; set; }

    /// <summary>How the subscription is billed ("automatic" vs. "remittance" for no stored card).</summary>
    public string? PaymentCollectionMethod { get; set; }
}
