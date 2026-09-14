using System.Globalization;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Projects the billing domain models onto the wire contracts of the subscription endpoints.
/// </summary>
public static class SubscriptionMapping
{
    public static SubscriptionPlanDto ToDto(this SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        PriceInCents = plan.PriceInCents,
        Price = plan.Price,
        Currency = NullIfEmpty(plan.Currency),
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        DisplayPrice = FormatPrice(plan.Price, plan.Currency, plan.Interval, plan.IntervalUnit),
        RequiresPaymentMethod = plan.RequiresPaymentMethod,
        HasTrial = plan.HasTrial,
        TrialInterval = plan.TrialInterval,
        TrialIntervalUnit = plan.TrialIntervalUnit,
        PricePointHandle = plan.PricePointHandle,
        ProductFamilyHandle = plan.ProductFamilyHandle
    };

    public static SubscriptionDto ToDto(this CustomerSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        IsActive = subscription.IsLive,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        PriceInCents = subscription.PriceInCents,
        Price = subscription.Price,
        Currency = NullIfEmpty(subscription.Currency),
        Interval = subscription.Interval,
        IntervalUnit = subscription.IntervalUnit,
        DisplayPrice = FormatPrice(subscription.Price, subscription.Currency, subscription.Interval,
            subscription.IntervalUnit),
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingAt = subscription.NextBillingAt,
        ActivatedAt = subscription.ActivatedAt,
        CanceledAt = subscription.CanceledAt,
        BalanceInCents = subscription.BalanceInCents,
        CustomerId = subscription.CustomerId,
        CustomerReference = subscription.CustomerReference,
        CreatedAt = subscription.CreatedAt
    };

    /// <summary>
    /// Renders an amount and cadence for display, e.g. "$299.00 / month" or "29.00 EUR / 3 months".
    /// </summary>
    private static string FormatPrice(decimal amount, string? currency, int? interval, string? intervalUnit)
    {
        var money = string.Equals(currency, "USD", System.StringComparison.OrdinalIgnoreCase)
            ? amount.ToString("C2", CultureInfo.GetCultureInfo("en-US"))
            : string.IsNullOrWhiteSpace(currency)
                ? amount.ToString("0.00", CultureInfo.InvariantCulture)
                : $"{amount.ToString("0.00", CultureInfo.InvariantCulture)} {currency}";

        if (string.IsNullOrWhiteSpace(intervalUnit))
        {
            return money;
        }

        var count = interval ?? 1;
        var cadence = count == 1 ? intervalUnit! : $"{count} {intervalUnit}s";
        return $"{money} / {cadence}";
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
