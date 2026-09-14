using System.Globalization;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Maps subscription domain models to API DTOs, including price formatting.
/// </summary>
internal static class SubscriptionMappings
{
    // The seeded sandbox catalog is priced in USD; format prices accordingly.
    private static readonly CultureInfo PriceCulture = CultureInfo.GetCultureInfo("en-US");

    public static string FormatPrice(int cents) => (cents / 100m).ToString("C", PriceCulture);

    public static SubscriptionPlanDto ToDto(this SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        PriceInCents = plan.PriceInCents,
        FormattedPrice = FormatPrice(plan.PriceInCents),
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        RequiresPaymentMethod = plan.RequiresPaymentMethod,
    };

    public static SubscriptionDto ToDto(this CustomerSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        PriceInCents = subscription.PriceInCents,
        FormattedPrice = FormatPrice(subscription.PriceInCents),
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingAt = subscription.NextBillingAt,
        ActivatedAt = subscription.ActivatedAt,
        PaymentCollectionMethod = subscription.PaymentCollectionMethod,
    };
}
