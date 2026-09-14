using System;
using System.Globalization;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Projects the domain view of subscriptions onto the wire contract. Hand written rather than
/// mapped by convention, because the DTOs deliberately add derived, display-ready fields.
/// </summary>
internal static class SubscriptionMapper
{
    public static SubscriptionPlanDto ToDto(this SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        Price = plan.Price,
        PriceInCents = plan.PriceInCents,
        Currency = plan.Currency,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        PriceDescription = DescribePrice(plan.Price, plan.Currency, plan.Interval, plan.IntervalUnit),
        TrialInterval = plan.TrialInterval,
        TrialIntervalUnit = plan.TrialIntervalUnit,
        SetupFee = plan.SetupFeeInCents is { } fee and > 0 ? fee / 100m : null,
        RequiresPaymentMethod = plan.RequiresPaymentMethod,
    };

    public static SubscriptionDto ToDto(this CustomerSubscription subscription) => new()
    {
        Id = subscription.Id,
        Reference = subscription.Reference,
        State = subscription.State,
        IsLive = subscription.IsLive,
        GrantsEntitlement = subscription.GrantsEntitlement,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        Price = subscription.Price,
        PriceInCents = subscription.PriceInCents,
        Currency = subscription.Currency,
        Interval = subscription.Interval,
        IntervalUnit = subscription.IntervalUnit,
        PriceDescription = DescribePrice(subscription.Price, subscription.Currency, subscription.Interval, subscription.IntervalUnit),
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingAt = subscription.NextBillingAt,
        ActivatedAt = subscription.ActivatedAt,
        CanceledAt = subscription.CanceledAt,
        CreatedAt = subscription.CreatedAt,
        Balance = subscription.Balance,
        PaymentCollectionMethod = subscription.PaymentCollectionMethod,
        CustomerId = subscription.CustomerId,
        CustomerReference = subscription.CustomerReference,
    };

    /// <summary>Renders "$299.00 / month" or "$299.00 / 3 months" for display.</summary>
    private static string DescribePrice(decimal price, string currency, int? interval, string? intervalUnit)
    {
        var amount = string.IsNullOrWhiteSpace(currency)
            ? price.ToString("0.00", CultureInfo.InvariantCulture)
            : $"{price.ToString("0.00", CultureInfo.InvariantCulture)} {currency}";

        if (interval is null || interval <= 0 || string.IsNullOrWhiteSpace(intervalUnit))
        {
            return amount;
        }

        var cadence = interval == 1
            ? intervalUnit
            : $"{interval} {intervalUnit}s";

        return $"{amount} / {cadence}";
    }
}
