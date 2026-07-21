using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Wire shape for <see cref="BillingPlan"/>.</summary>
public class BillingPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string BillingIntervalUnit { get; set; } = string.Empty;
    public int BillingIntervalCount { get; set; }
    public bool RequiresPaymentMethod { get; set; }

    public static BillingPlanDto FromDomain(BillingPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Price = plan.Price,
        BillingIntervalUnit = plan.BillingIntervalUnit,
        BillingIntervalCount = plan.BillingIntervalCount,
        RequiresPaymentMethod = plan.RequiresPaymentMethod
    };
}

/// <summary>Wire shape for <see cref="Subscription"/>.</summary>
public class SubscriptionDto
{
    public int Id { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public bool CancelAtEndOfPeriod { get; set; }

    public static SubscriptionDto FromDomain(Subscription subscription) => new()
    {
        Id = subscription.Id,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        Price = subscription.Price,
        Status = subscription.Status.ToString(),
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod
    };
}
