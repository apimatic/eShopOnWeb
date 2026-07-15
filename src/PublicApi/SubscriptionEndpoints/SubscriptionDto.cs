using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public int Id { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public int PriceInCents { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? NextBillingDate { get; set; }
    public bool CancelAtPeriodEnd { get; set; }
    public string? PendingPlanHandle { get; set; }

    public static SubscriptionDto FromDomain(BillingSubscription subscription) => new()
    {
        Id = subscription.Id,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        PriceInCents = subscription.PriceInCents,
        State = subscription.State.ToString(),
        NextBillingDate = subscription.NextBillingDate,
        CancelAtPeriodEnd = subscription.CancelAtPeriodEnd,
        PendingPlanHandle = subscription.PendingPlanHandle
    };
}
