using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>A subscription as exposed over the API.</summary>
public class SubscriptionDto
{
    public int Id { get; set; }

    /// <summary>The normalised lifecycle state, e.g. <c>Active</c>.</summary>
    public string Status { get; set; }

    /// <summary>The provider's own state string, retained so unmodelled states stay visible.</summary>
    public string? ProviderState { get; set; }

    public string? PlanHandle { get; set; }

    public string? PlanName { get; set; }

    /// <summary>The plan price as a currency amount.</summary>
    public decimal? PlanPrice { get; set; }

    public DateTimeOffset? NextBillingDate { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? CanceledAt { get; set; }

    public bool CancelAtEndOfPeriod { get; set; }

    public DateTimeOffset? DelayedCancelAt { get; set; }

    /// <summary>Set when a plan change has been scheduled for the next renewal.</summary>
    public string? NextPlanHandle { get; set; }

    public static SubscriptionDto FromSubscription(CustomerSubscription subscription)
    {
        return new SubscriptionDto
        {
            Id = subscription.Id,
            Status = subscription.Status.ToString(),
            ProviderState = subscription.ProviderState,
            PlanHandle = subscription.PlanHandle,
            PlanName = subscription.PlanName,
            PlanPrice = subscription.PlanPrice,
            NextBillingDate = subscription.NextBillingDate,
            ActivatedAt = subscription.ActivatedAt,
            CanceledAt = subscription.CanceledAt,
            CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod,
            DelayedCancelAt = subscription.DelayedCancelAt,
            NextPlanHandle = subscription.NextPlanHandle
        };
    }
}
