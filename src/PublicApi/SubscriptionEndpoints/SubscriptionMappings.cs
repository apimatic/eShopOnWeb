using System.Globalization;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Maps the ApplicationCore subscription models to the API DTOs. Kept as a small explicit
/// mapper (rather than an AutoMapper profile) so the money/format shaping is obvious.
/// </summary>
internal static class SubscriptionMappings
{
    public static SubscriptionPlanDto ToDto(this SubscriptionPlan plan)
    {
        var price = plan.PriceInCents / 100m;
        return new SubscriptionPlanDto
        {
            ProductId = plan.ProductId,
            Handle = plan.Handle,
            Name = plan.Name,
            Description = plan.Description,
            PriceInCents = plan.PriceInCents,
            Price = price,
            FormattedPrice = price.ToString("0.00", CultureInfo.InvariantCulture),
            Interval = plan.Interval,
            IntervalUnit = plan.IntervalUnit,
            BillingCadence = FormatCadence(plan.Interval, plan.IntervalUnit),
            RequiresPaymentMethod = plan.RequiresPaymentMethod,
        };
    }

    public static CustomerSubscriptionDto ToDto(this CustomerSubscription subscription)
    {
        var price = subscription.PriceInCents / 100m;
        return new CustomerSubscriptionDto
        {
            Id = subscription.Id,
            State = subscription.State,
            PlanHandle = subscription.PlanHandle,
            PlanName = subscription.PlanName,
            PriceInCents = subscription.PriceInCents,
            Price = price,
            Currency = subscription.Currency,
            FormattedPrice = $"{price.ToString("0.00", CultureInfo.InvariantCulture)} {subscription.Currency}".Trim(),
            CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            NextBillingDate = subscription.NextBillingAt,
            CustomerId = subscription.CustomerId,
            CustomerReference = subscription.CustomerReference,
        };
    }

    private static string FormatCadence(int interval, string unit)
        => interval == 1 ? $"every {unit}" : $"every {interval} {unit}s";
}
