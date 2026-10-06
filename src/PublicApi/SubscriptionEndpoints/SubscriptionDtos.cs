namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

using System;

/// <summary>
/// A subscription plan available for purchase, sourced from the Maxio product
/// family configured for eShopOnWeb subscriptions.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>
    /// The Maxio product handle. Pass it as planHandle when subscribing.
    /// </summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>
    /// The recurring price in cents.
    /// </summary>
    public long PriceInCents { get; set; }

    /// <summary>
    /// The recurring price formatted as a decimal string, e.g. "299.00".
    /// </summary>
    public string Price { get; set; } = string.Empty;

    /// <summary>
    /// The billing interval, e.g. 1.
    /// </summary>
    public int Interval { get; set; }

    /// <summary>
    /// The billing interval unit, e.g. "month".
    /// </summary>
    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>
    /// True when the plan requires a payment method at signup.
    /// </summary>
    public bool RequireCreditCard { get; set; }
}

/// <summary>
/// A subscription owned by the authenticated user, as recorded in Maxio.
/// </summary>
public class SubscriptionDto
{
    /// <summary>
    /// The Maxio subscription id.
    /// </summary>
    public int SubscriptionId { get; set; }

    /// <summary>
    /// The Maxio product handle of the subscribed plan.
    /// </summary>
    public string PlanHandle { get; set; } = string.Empty;

    public string PlanName { get; set; } = string.Empty;

    /// <summary>
    /// The subscription state in Maxio, e.g. active, trialing, past_due, canceled.
    /// </summary>
    public string State { get; set; } = string.Empty;

    public long PriceInCents { get; set; }

    public string Price { get; set; } = string.Empty;

    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>
    /// When the next billing event is scheduled.
    /// </summary>
    public DateTime? NextBillingDate { get; set; }

    public DateTime? CreatedAt { get; set; }
}