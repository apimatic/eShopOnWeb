using System;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>A shopper's subscription as returned to the eShopOnWeb client.</summary>
public class SubscriptionDto
{
    public int Id { get; set; }

    /// <summary>Maxio subscription state, e.g. "active", "trialing", "canceled".</summary>
    public string? State { get; set; }

    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }

    /// <summary>Recurring plan price in integer cents.</summary>
    public long PlanPriceInCents { get; set; }

    /// <summary>Recurring plan price in major currency units (cents / 100).</summary>
    public decimal PlanPrice { get; set; }

    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }

    /// <summary>End of the current billing period (when the next scheduled charge occurs).</summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>When Maxio will next attempt to capture payment (retry-aware).</summary>
    public DateTimeOffset? NextAssessmentAt { get; set; }

    /// <summary>The next billing date, i.e. next payment attempt falling back to the period end.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }

    public int? CustomerId { get; set; }
    public string? CustomerReference { get; set; }

    public static SubscriptionDto FromMaxio(MaxioSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        PlanHandle = subscription.Product?.Handle,
        PlanName = subscription.Product?.Name,
        PlanPriceInCents = subscription.ProductPriceInCents,
        PlanPrice = subscription.ProductPriceInCents / 100m,
        Interval = subscription.Product?.Interval ?? 0,
        IntervalUnit = subscription.Product?.IntervalUnit,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextAssessmentAt = subscription.NextAssessmentAt,
        NextBillingAt = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
        ActivatedAt = subscription.ActivatedAt,
        CreatedAt = subscription.CreatedAt,
        CustomerId = subscription.Customer?.Id,
        CustomerReference = subscription.Customer?.Reference
    };
}
