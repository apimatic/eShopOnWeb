using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// One of the caller's subscriptions, as the billing system of record currently reports it.
/// </summary>
public class SubscriptionDto
{
    public int Id { get; set; }

    public string? PlanHandle { get; set; }

    public string PlanName { get; set; } = string.Empty;

    /// <summary>Recurring price in major currency units.</summary>
    public decimal? Price { get; set; }

    /// <summary>Recurring price in minor currency units, exactly as the billing provider reports it.</summary>
    public long? PriceInCents { get; set; }

    public string? Currency { get; set; }

    /// <summary>The provider's own state value, e.g. <c>active</c>.</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>When the subscription is next assessed.</summary>
    public DateTimeOffset? NextBillingDate { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }
}
