using System;
using Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Maps subscription domain results to PublicApi DTOs.
/// </summary>
internal static class SubscriptionMapper
{
    public static SubscriptionPlanDto ToDto(SubscriptionPlan plan)
    {
        return new SubscriptionPlanDto
        {
            Id = plan.Id,
            Handle = plan.Handle,
            Name = plan.Name,
            Description = plan.Description,
            PriceInCents = plan.PriceInCents,
            Currency = plan.Currency,
            Interval = plan.Interval,
            IntervalUnit = plan.IntervalUnit
        };
    }

    public static SubscriptionDto ToDto(SubscriptionPurchase purchase)
    {
        return new SubscriptionDto
        {
            Id = purchase.SubscriptionId,
            State = purchase.State,
            PlanHandle = purchase.PlanHandle,
            PlanName = purchase.PlanName,
            ProductPriceInCents = purchase.ProductPriceInCents,
            Currency = purchase.Currency,
            CurrentPeriodEndsAt = purchase.CurrentPeriodEndsAt,
            NextBillingAt = purchase.NextBillingAt,
            ActivatedAt = purchase.ActivatedAt,
            CreatedAt = purchase.CreatedAt,
            BalanceInCents = purchase.BalanceInCents
        };
    }
}
