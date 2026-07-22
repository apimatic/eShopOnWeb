using System;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Translates the subscription domain types into the API's wire contract and identifies the caller
/// from the JWT the PublicApi issues.
/// </summary>
public static class SubscriptionMapper
{
    public static SubscriptionPlanDto ToDto(this BillingPlan plan) => new()
    {
        Id = plan.Id,
        Handle = plan.Handle,
        Name = plan.Name,
        Price = plan.Price,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        RequiresPaymentMethod = plan.RequiresPaymentMethod
    };

    public static SubscriptionDto ToDto(this BillingSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State.ToString(),
        CustomerReference = subscription.CustomerReference,
        PlanId = subscription.PlanId,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        PlanPrice = subscription.PlanPrice,
        Balance = subscription.Balance,
        Currency = subscription.Currency,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingDate = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
        CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod,
        ScheduledCancellationAt = subscription.ScheduledCancellationAt,
        NextPlanHandle = subscription.NextPlanHandle
    };

    public static UsageRecordDto ToDto(this UsageRecord record) => new()
    {
        Id = record.Id,
        Quantity = record.Quantity,
        Memo = record.Memo,
        RecordedAt = record.RecordedAt,
        ComponentId = record.ComponentId,
        ComponentHandle = record.ComponentHandle
    };

    public static PlanChangePreviewDto ToDto(this PlanChangePreview preview) => new()
    {
        CurrentPlanHandle = preview.CurrentPlanHandle,
        TargetPlanHandle = preview.TargetPlanHandle,
        Timing = preview.Timing.ToString(),
        ProratedAdjustment = preview.ProratedAdjustment,
        Charge = preview.Charge,
        PaymentDue = preview.PaymentDue,
        CreditApplied = preview.CreditApplied,
        TargetPlanPrice = preview.TargetPlanPrice,
        ProratedAdjustmentInCents = ToCents(preview.ProratedAdjustment),
        ChargeInCents = ToCents(preview.Charge),
        AmountDueInCents = ToCents(preview.PaymentDue),
        PaymentDueInCents = ToCents(preview.PaymentDue),
        CreditAppliedInCents = ToCents(preview.CreditApplied),
        TargetPlanPriceInCents = ToCents(preview.TargetPlanPrice)
    };

    /// <summary>Converts a major-unit amount back to the minor units the provider reports.</summary>
    public static long ToCents(decimal amount) => (long)decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Builds the actor from the bearer token. The user name is the stable customer reference used
    /// on the provider side (plan §4.4); administrators may act on any subscription.
    /// </summary>
    public static SubscriptionActor ToSubscriptionActor(this ClaimsPrincipal user) => new(
        user.Identity?.Name ?? string.Empty,
        user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS));
}
