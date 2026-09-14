using System.Globalization;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Projects the application's subscription model onto the API contract. Kept explicit rather than
/// convention-mapped because money and dates are the part of this API callers are least forgiving about.
/// </summary>
public static class SubscriptionMapping
{
    private const int MinorUnitsPerMajorUnit = 100;

    public static SubscriptionPlanDto ToDto(this SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        Price = ToMajorUnits(plan.PriceInCents),
        PriceInCents = plan.PriceInCents,
        Currency = plan.Currency,
        FormattedPrice = FormatPrice(plan.PriceInCents, plan.Currency, plan.Interval),
        BillingIntervalLength = plan.Interval.Length,
        BillingIntervalUnit = plan.Interval.Unit,
        RequiresPaymentMethod = plan.RequiresPaymentMethod,
        HasTrial = plan.HasTrial,
        TrialIntervalLength = plan.TrialInterval,
        TrialIntervalUnit = plan.TrialIntervalUnit,
        ProductFamilyHandle = plan.ProductFamilyHandle,
    };

    public static CustomerSubscriptionDto ToDto(this CustomerSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        IsActive = subscription.IsHealthy,
        Reference = subscription.Reference,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        Price = ToMajorUnits(subscription.PriceInCents),
        PriceInCents = subscription.PriceInCents,
        Currency = subscription.Currency,
        FormattedPrice = FormatPrice(subscription.PriceInCents, subscription.Currency, subscription.Interval),
        BillingIntervalLength = subscription.Interval?.Length,
        BillingIntervalUnit = subscription.Interval?.Unit,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingAt = subscription.NextBillingAt,
        CreatedAt = subscription.CreatedAt,
        ActivatedAt = subscription.ActivatedAt,
        CanceledAt = subscription.CanceledAt,
        ExpiresAt = subscription.ExpiresAt,
        BalanceInCents = subscription.BalanceInCents,
        PaymentCollectionMethod = subscription.PaymentCollectionMethod,
        CustomerId = subscription.CustomerId,
    };

    private static decimal ToMajorUnits(long minorUnits) => minorUnits / (decimal)MinorUnitsPerMajorUnit;

    /// <summary>
    /// Formats as "299.00 USD / month". The ISO code is used instead of a locale-derived symbol so the
    /// string means the same thing to every caller, whatever culture the process happens to run under.
    /// </summary>
    private static string FormatPrice(long priceInCents, string currency, BillingInterval? interval)
    {
        var amount = ToMajorUnits(priceInCents).ToString("0.00", CultureInfo.InvariantCulture);

        return interval is null
            ? $"{amount} {currency}"
            : $"{amount} {currency} / {interval.ToDisplayString()}";
    }
}
