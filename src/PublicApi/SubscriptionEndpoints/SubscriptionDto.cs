using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public int SubscriptionId { get; set; }
    public int CustomerId { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;

    /// <summary>The plan price in major currency units, e.g. 299.00.</summary>
    public decimal PlanPrice { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
    public bool CancelAtEndOfPeriod { get; set; }

    public static SubscriptionDto From(Subscription subscription) => new()
    {
        SubscriptionId = subscription.BillingSubscriptionId,
        CustomerId = subscription.BillingCustomerId,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        PlanPrice = subscription.PlanPrice,
        State = subscription.State,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingDate = subscription.NextBillingAt,
        CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod
    };
}
