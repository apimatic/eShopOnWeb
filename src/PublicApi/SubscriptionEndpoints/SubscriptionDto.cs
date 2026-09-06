using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A shopper's enrollment in a subscription plan.
/// </summary>
public class SubscriptionDto
{
    public int Id { get; set; }

    /// <summary>The idempotency key this subscription was created with.</summary>
    public string? Reference { get; set; }

    public string? PlanHandle { get; set; }

    public string? PlanName { get; set; }

    /// <summary>Lifecycle state, for example <c>active</c> or <c>trialing</c>.</summary>
    public string? State { get; set; }

    /// <summary>False once the subscription has been cancelled or has expired.</summary>
    public bool IsActive { get; set; }

    public long? PriceInCents { get; set; }

    public decimal? Price { get; set; }

    public string? Currency { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>When the subscription will next be billed.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public DateTimeOffset? TrialEndedAt { get; set; }

    public DateTimeOffset? CanceledAt { get; set; }
}
