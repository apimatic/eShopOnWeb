using System;
using Microsoft.eShopWeb.MaxioBilling.Models;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Maps billing models onto the API's own DTOs. Written by hand rather than through the
/// catalog's AutoMapper profile: these are an external system's shapes, and the cents-to-major-
/// units conversion is the kind of thing that should be visible at the boundary.
/// </summary>
internal static class SubscriptionMappings
{
    public static SubscriptionPlanDto ToDto(this PlanSummary plan, string? defaultPlanHandle) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        Price = ToMajorUnits(plan.PriceInCents),
        PriceInCents = plan.PriceInCents,
        Currency = plan.Currency,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        HasTrial = plan.HasTrial,
        TrialInterval = plan.TrialInterval,
        TrialIntervalUnit = plan.TrialIntervalUnit,
        SetupFee = ToMajorUnits(plan.SetupFeeInCents),
        PaymentMethodRequired = plan.PaymentMethodRequired,
        PaymentMethodRequested = plan.PaymentMethodRequested,
        IsDefault = !string.IsNullOrWhiteSpace(defaultPlanHandle) &&
                    string.Equals(plan.Handle, defaultPlanHandle, StringComparison.OrdinalIgnoreCase)
    };

    public static SubscriptionDto ToDto(this SubscriptionSummary subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        Price = ToMajorUnits(subscription.PriceInCents),
        PriceInCents = subscription.PriceInCents,
        Currency = subscription.Currency,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingAt = subscription.NextAssessmentAt,
        ActivatedAt = subscription.ActivatedAt,
        CanceledAt = subscription.CanceledAt,
        CustomerId = subscription.CustomerId,
        CustomerReference = subscription.CustomerReference,
        Reference = subscription.Reference
    };

    private static decimal? ToMajorUnits(long? minorUnits) =>
        minorUnits is null ? null : decimal.Divide(minorUnits.Value, 100m);
}
