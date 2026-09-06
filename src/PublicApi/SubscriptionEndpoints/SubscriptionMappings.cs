using System;
using System.Globalization;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using DomainSubscription = Microsoft.eShopWeb.ApplicationCore.Subscriptions.Subscription;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Projects the billing domain models onto the wire contracts for the subscription endpoints.
/// </summary>
internal static class SubscriptionMappings
{
    public static SubscriptionPlanDto ToDto(this SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        Price = plan.Price,
        Currency = plan.Currency,
        Interval = plan.Interval.Length,
        IntervalUnit = UnitName(plan.Interval.Unit),
        BillingPeriod = DescribeBilling(plan.Price, plan.Currency, plan.Interval),
        RequiresPaymentMethod = plan.RequiresPaymentMethod,
        ProductFamilyHandle = plan.ProductFamilyHandle,
        Trial = plan.TrialDescription
    };

    public static SubscriptionDto ToDto(this DomainSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State.ToString(),
        ProviderState = subscription.ProviderState,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        Price = subscription.Price,
        Currency = subscription.Currency,
        Interval = subscription.Interval.Length,
        IntervalUnit = UnitName(subscription.Interval.Unit),
        BillingPeriod = DescribeBilling(subscription.Price, subscription.Currency, subscription.Interval),
        NextBillingAt = subscription.NextBillingAt,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        CanceledAt = subscription.CanceledAt,
        CreatedAt = subscription.CreatedAt,
        CustomerId = subscription.CustomerId,
        CustomerReference = subscription.CustomerReference,
        Reference = subscription.Reference
    };

    private static string UnitName(BillingIntervalUnit unit) => unit.ToString().ToLowerInvariant();

    private static string DescribeBilling(decimal price, string currency, BillingInterval interval)
    {
        var amount = $"{price.ToString("0.00", CultureInfo.InvariantCulture)} {currency}".Trim();
        return interval.Unit == BillingIntervalUnit.Unknown ? amount : $"{amount} {interval}";
    }
}
