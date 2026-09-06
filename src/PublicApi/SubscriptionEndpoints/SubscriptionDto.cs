using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>A shopper's enrolment in a plan, as held by the billing system of record.</summary>
public class SubscriptionDto
{
    /// <summary>The billing provider's subscription id.</summary>
    public int Id { get; set; }

    /// <summary>The reference eShopOnWeb assigned to this subscription; unique per billing site.</summary>
    public string? Reference { get; set; }

    /// <summary>Lifecycle state, e.g. <c>active</c>, <c>past_due</c>, <c>canceled</c>.</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>True while the subscription still entitles the shopper to the plan.</summary>
    public bool IsLive { get; set; }

    public string PlanHandle { get; set; } = string.Empty;

    public string PlanName { get; set; } = string.Empty;

    /// <summary>Recurring price in minor currency units.</summary>
    public long PriceInCents { get; set; }

    /// <summary>Convenience rendering of <see cref="PriceInCents"/> in major units.</summary>
    public decimal Price { get; set; }

    public string Currency { get; set; } = string.Empty;

    public BillingIntervalDto Interval { get; set; } = new();

    /// <summary>When payment is next attempted. Null once the subscription has ended.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? CanceledAt { get; set; }

    public DateTimeOffset? TrialEndsAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Outstanding balance in minor currency units.</summary>
    public long BalanceInCents { get; set; }

    /// <summary>How the provider collects payment, e.g. <c>automatic</c> or <c>remittance</c>.</summary>
    public string? PaymentCollectionMethod { get; set; }

    /// <summary>The provider's customer id backing this subscription.</summary>
    public int CustomerId { get; set; }
}
