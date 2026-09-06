using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// One of the caller's subscriptions, as reported by the billing system of record.
/// </summary>
public class SubscriptionDto
{
    public int? Id { get; set; }

    /// <summary>Deterministic reference assigned at enrollment.</summary>
    public string? Reference { get; set; }

    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }

    public long? PriceInCents { get; set; }
    public decimal? Price { get; set; }
    public string? Currency { get; set; }

    /// <summary>Billing-system state, e.g. "active", "trialing", "canceled".</summary>
    public string? State { get; set; }

    /// <summary>True when the state entitles the caller to the plan right now.</summary>
    public bool IsActive { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>When the next charge is expected.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }

    public int? CustomerId { get; set; }
    public string? CustomerReference { get; set; }
}
