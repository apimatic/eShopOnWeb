using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A shopper's subscription, as reported by the billing provider.
/// </summary>
public class CustomerSubscriptionDto
{
    /// <summary>The billing provider's subscription id.</summary>
    public int Id { get; set; }

    /// <summary>The reference eShopOnWeb assigned to the subscription; it is what makes subscribing idempotent.</summary>
    public string? Reference { get; set; }

    /// <summary>Provider state, e.g. active, trialing, past_due, canceled.</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>True while the subscription still entitles the shopper to the plan.</summary>
    public bool IsLive { get; set; }

    public string? PlanHandle { get; set; }

    public string? PlanName { get; set; }

    public decimal Price { get; set; }

    public long PriceInCents { get; set; }

    public string? Currency { get; set; }

    public int Interval { get; set; }

    public string? IntervalUnit { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>When the subscription is next billed.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? CanceledAt { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>How the provider collects payment, e.g. automatic or remittance (invoice).</summary>
    public string? PaymentCollectionMethod { get; set; }
}
