using System.Globalization;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Maps ApplicationCore subscription read models onto the API DTOs.</summary>
internal static class SubscriptionMapping
{
    public static SubscriptionPlanDto ToDto(this SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        PriceInCents = plan.PriceInCents,
        Currency = plan.Currency,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        ProductFamilyHandle = plan.ProductFamilyHandle,
        FormattedPrice = FormatPrice(plan.PriceInCents, plan.Currency, plan.Interval, plan.IntervalUnit)
    };

    public static SubscriptionDto ToDto(this CustomerSubscription subscription) => new()
    {
        SubscriptionId = subscription.SubscriptionId,
        State = subscription.State,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        PriceInCents = subscription.PriceInCents,
        Currency = subscription.Currency,
        NextBillingAt = subscription.NextBillingAt,
        CreatedAt = subscription.CreatedAt,
        CustomerId = subscription.CustomerId,
        FormattedPrice = FormatPrice(subscription.PriceInCents, subscription.Currency, interval: 0, intervalUnit: null)
    };

    private static string FormatPrice(long priceInCents, string currency, int interval, string? intervalUnit)
    {
        var symbol = currency == "USD" ? "$" : string.Empty;
        var amount = (priceInCents / 100m).ToString("0.00", CultureInfo.InvariantCulture);
        var money = $"{symbol}{amount}";
        if (string.IsNullOrEmpty(symbol))
            money = $"{amount} {currency}";

        if (string.IsNullOrWhiteSpace(intervalUnit))
            return money;

        var period = interval > 1 ? $"{interval} {intervalUnit}s" : intervalUnit;
        return $"{money}/{period}";
    }
}
