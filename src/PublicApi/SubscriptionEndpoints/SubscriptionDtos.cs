using System;
using System.Globalization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Shared mapping helpers for the subscription endpoints.
/// </summary>
public static class SubscriptionDtos
{
    public static SubscriptionPlanDto FromPlan(ApplicationCore.Models.MaxioBilling.SubscriptionPlanInfo plan) =>
        new SubscriptionPlanDto
        {
            Handle = plan.Handle,
            Name = plan.Name,
            Description = plan.Description,
            PriceInCents = plan.PriceInCents,
            PriceDisplay = FormatPrice(plan.PriceInCents),
            BillingInterval = FormatInterval(plan.Interval, plan.IntervalUnit),
            RequireCreditCard = plan.RequireCreditCard
        };

    public static SubscriptionDto FromSubscription(ApplicationCore.Models.MaxioBilling.SubscriptionInfo subscription) =>
        new SubscriptionDto
        {
            SubscriptionId = subscription.MaxioSubscriptionId,
            ProductHandle = subscription.ProductHandle,
            ProductName = subscription.ProductName,
            State = subscription.State,
            PriceInCents = subscription.PriceInCents,
            PriceDisplay = subscription.PriceInCents.HasValue ? FormatPrice(subscription.PriceInCents.Value) : null,
            NextBillingAt = subscription.CurrentPeriodEndsAt,
            Reference = subscription.Reference
        };

    public static string FormatPrice(long priceInCents) =>
        $"${(priceInCents / 100m).ToString("0.00", CultureInfo.InvariantCulture)}";

    public static string FormatInterval(int interval, string intervalUnit) =>
        interval == 1
            ? $"every 1 {intervalUnit}"
            : $"every {interval} {intervalUnit}s";
}