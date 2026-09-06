using System.Globalization;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Projects the application's subscription model onto the API's wire contract.</summary>
public static class SubscriptionMappings
{
    /// <summary>
    /// Maxio states prices in the currency's minor unit; this converts to major units for display
    /// without losing the exact <c>PriceInCents</c> value the caller can rely on.
    /// </summary>
    private const decimal MinorUnitsPerMajorUnit = 100m;

    public static SubscriptionPlanDto ToDto(this SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        PriceInCents = plan.PriceInCents,
        Price = plan.PriceInCents / MinorUnitsPerMajorUnit,
        Currency = plan.Currency,
        Interval = plan.Interval.ToDto(),
        Summary = BuildSummary(plan),
        SetupFeeInCents = plan.SetupFeeInCents,
        Trial = plan.Trial?.ToDto(),
        TrialPriceInCents = plan.TrialPriceInCents,
        RequiresPaymentMethod = plan.RequiresPaymentMethod,
        Taxable = plan.Taxable,
        ProductFamilyHandle = plan.ProductFamilyHandle,
        ProductId = plan.ProductId
    };

    public static SubscriptionDto ToDto(this Subscription subscription) => new()
    {
        Id = subscription.Id,
        Reference = subscription.Reference,
        State = subscription.RawState,
        IsLive = subscription.IsLive,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        PriceInCents = subscription.PriceInCents,
        Price = subscription.PriceInCents / MinorUnitsPerMajorUnit,
        Currency = subscription.Currency,
        Interval = subscription.Interval.ToDto(),
        NextBillingAt = subscription.NextBillingAt,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        ActivatedAt = subscription.ActivatedAt,
        CanceledAt = subscription.CanceledAt,
        TrialEndsAt = subscription.TrialEndsAt,
        CreatedAt = subscription.CreatedAt,
        BalanceInCents = subscription.BalanceInCents,
        PaymentCollectionMethod = subscription.PaymentCollectionMethod,
        CustomerId = subscription.CustomerId
    };

    public static BillingIntervalDto ToDto(this BillingInterval interval) => new()
    {
        Length = interval.Length,
        Unit = interval.Unit
    };

    private static string BuildSummary(SubscriptionPlan plan)
    {
        var amount = (plan.PriceInCents / MinorUnitsPerMajorUnit).ToString("0.00", CultureInfo.InvariantCulture);
        var money = string.IsNullOrEmpty(plan.Currency) ? amount : $"{plan.Currency} {amount}";

        return $"{money} {plan.Interval}";
    }
}
