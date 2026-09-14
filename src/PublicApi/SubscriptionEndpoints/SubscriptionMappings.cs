using System.Linq;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionMappings
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
            Interval = plan.Interval,
            IntervalUnit = plan.IntervalUnit,
            RequiresPaymentMethod = plan.RequiresPaymentMethod,
            Taxable = plan.Taxable
        };
    }

    public static SubscriptionDto ToDto(SubscriptionEnrollment enrollment)
    {
        return new SubscriptionDto
        {
            Id = enrollment.Id,
            State = enrollment.State,
            PlanHandle = enrollment.ProductHandle,
            PlanName = enrollment.ProductName,
            Reference = enrollment.Reference,
            Currency = enrollment.Currency,
            BalanceInCents = enrollment.BalanceInCents,
            PriceInCents = enrollment.PriceInCents,
            PaymentCollectionMethod = enrollment.PaymentCollectionMethod,
            NextBillingDate = enrollment.NextAssessmentAt ?? enrollment.CurrentPeriodEndsAt,
            CurrentPeriodStartedAt = enrollment.CurrentPeriodStartedAt,
            CurrentPeriodEndsAt = enrollment.CurrentPeriodEndsAt,
            NextAssessmentAt = enrollment.NextAssessmentAt,
            ActivatedAt = enrollment.ActivatedAt,
            CreatedAt = enrollment.CreatedAt,
            CanceledAt = enrollment.CanceledAt,
            CancelAtEndOfPeriod = enrollment.CancelAtEndOfPeriod
        };
    }
}
