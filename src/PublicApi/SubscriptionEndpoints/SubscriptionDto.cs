using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>A shopper's enrolment in a subscription plan, as held by the billing provider.</summary>
public class SubscriptionDto
{
    /// <summary>The billing provider's subscription id.</summary>
    public long Id { get; set; }

    /// <summary>The reference eShopOnWeb assigned to this subscription.</summary>
    public string? Reference { get; set; }

    /// <summary>Provider state, e.g. "active", "trialing", "canceled".</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>True while the subscription still entitles the shopper to the plan.</summary>
    public bool IsLive { get; set; }

    public string? PlanHandle { get; set; }

    public string? PlanName { get; set; }

    /// <summary>Recurring price in the smallest currency unit.</summary>
    public long PriceInCents { get; set; }

    /// <summary>Recurring price as a decimal amount.</summary>
    public decimal Price { get; set; }

    public string? Currency { get; set; }

    public int Interval { get; set; }

    public string? IntervalUnit { get; set; }

    public DateTimeOffset? CurrentPeriodStartsAt { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>When the plan will next be billed.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? CanceledAt { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>Outstanding balance in the smallest currency unit.</summary>
    public long BalanceInCents { get; set; }

    /// <summary>How the provider collects payment, e.g. "remittance".</summary>
    public string? PaymentCollectionMethod { get; set; }

    /// <summary>The billing provider's customer id for this shopper.</summary>
    public long CustomerId { get; set; }

    /// <summary>The reference eShopOnWeb assigned to this shopper's billing customer.</summary>
    public string? CustomerReference { get; set; }
}
