using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A shopper's subscription, as held by the billing system of record.
/// </summary>
public class SubscriptionDto
{
    /// <summary>The billing provider's subscription id.</summary>
    public int Id { get; set; }

    /// <summary>Provider state, e.g. <c>active</c>, <c>trialing</c>, <c>past_due</c>, <c>canceled</c>.</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>False once the subscription reaches a terminal state and will never bill again.</summary>
    public bool IsLive { get; set; }

    /// <summary>The reference this application stamped on the subscription.</summary>
    public string? Reference { get; set; }

    /// <summary>Handle of the subscribed plan.</summary>
    public string? PlanHandle { get; set; }

    /// <summary>Name of the subscribed plan.</summary>
    public string? PlanName { get; set; }

    public int? PlanId { get; set; }

    /// <summary>Recurring amount currently charged, in minor currency units.</summary>
    public long PriceInCents { get; set; }

    /// <summary>Recurring amount as a decimal.</summary>
    public decimal Price { get; set; }

    public string? Currency { get; set; }

    /// <summary>When the provider will next attempt to bill this subscription.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? CanceledAt { get; set; }

    public DateTimeOffset? TrialStartedAt { get; set; }

    public DateTimeOffset? TrialEndedAt { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    public long BalanceInCents { get; set; }

    public long TotalRevenueInCents { get; set; }

    public string? PaymentCollectionMethod { get; set; }

    public BillingCustomerDto? Customer { get; set; }
}
