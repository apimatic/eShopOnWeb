using System.Globalization;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Projects the provider-neutral billing models onto the API's response shapes.
/// </summary>
/// <remarks>
/// Hand-written rather than AutoMapper-configured: every field here needs a decision the mapper cannot
/// make — cents to a decimal, an interval count plus unit to a readable period, and a null price left
/// as null rather than rendered as zero, because "the billing system reported no price" is not the same
/// statement as "this plan is free".
/// </remarks>
internal static class SubscriptionMapping
{
    public static SubscriptionPlanDto ToDto(this SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        PriceInCents = plan.PriceInCents,
        Price = ToAmount(plan.PriceInCents),
        PriceDisplay = ToDisplay(plan.PriceInCents),
        BillingPeriod = ToPeriod(plan.Interval, plan.IntervalUnit),
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        HasTrial = plan.TrialInterval is > 0,
        TrialPeriod = plan.TrialInterval is > 0 ? ToPeriod(plan.TrialInterval, plan.TrialIntervalUnit) : null,
        SetupFee = ToAmount(plan.SetupFeeInCents),
        RequiresCreditCard = plan.RequiresCreditCard,
        Taxable = plan.Taxable
    };

    public static SubscriptionDto ToDto(this CustomerSubscription subscription) => new()
    {
        Id = subscription.Id,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        State = subscription.State,
        IsLive = subscription.IsLive,
        PaymentCollectionMethod = subscription.PaymentCollectionMethod,
        PriceInCents = subscription.PriceInCents,
        Price = ToAmount(subscription.PriceInCents),
        PriceDisplay = ToDisplay(subscription.PriceInCents),
        BillingPeriod = ToPeriod(subscription.Interval, subscription.IntervalUnit),
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        NextBillingAt = subscription.NextBillingAt,
        NextAssessmentAt = subscription.NextAssessmentAt,
        CreatedAt = subscription.CreatedAt,
        ActivatedAt = subscription.ActivatedAt,
        CanceledAt = subscription.CanceledAt
    };

    public static PlanComponentDto ToDto(this PlanComponent component) => new()
    {
        Handle = component.Handle,
        Name = component.Name,
        Kind = component.Kind,
        UnitName = component.UnitName,
        PricePerUnitInCents = component.PricePerUnitInCents,
        UnitPrice = component.UnitPrice,
        PricePerUnit = ToAmount(component.PricePerUnitInCents),
        PricePerUnitDisplay = ToDisplay(component.PricePerUnitInCents),
        PricingScheme = component.PricingScheme,
        Recurring = component.Recurring
    };

    private static decimal? ToAmount(long? cents) => cents is null ? null : cents.Value / 100m;

    private static string? ToDisplay(long? cents) =>
        cents is null ? null : (cents.Value / 100m).ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>
    /// Renders an interval count and unit. Always built from both parts: the billing system has no
    /// yearly unit, so an annual plan arrives as twelve months and hard-coding "month" would misreport it.
    /// </summary>
    private static string? ToPeriod(int? interval, string? unit)
    {
        if (interval is not { } count || string.IsNullOrWhiteSpace(unit))
        {
            return null;
        }

        return count == 1 ? $"1 {unit}" : $"{count} {unit}s";
    }
}
