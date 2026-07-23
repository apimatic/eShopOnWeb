using System.Linq;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Services;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Translates subscription domain types into the API's DTOs, and the caller's JWT identity into the
/// actor the domain authorizes against.
/// </summary>
internal static class SubscriptionMappings
{
    /// <summary>
    /// Builds the acting identity from the bearer token. Administrators may act on any subscription;
    /// everyone else is confined to their own (plan.md §2.4).
    /// </summary>
    public static SubscriptionActor ToActor(this ClaimsPrincipal user)
    {
        var userName = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new System.UnauthorizedAccessException("The bearer token carries no user name.");
        }

        return user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS)
            ? SubscriptionActor.Administrator(userName)
            : SubscriptionActor.Customer(userName);
    }

    public static SubscriptionPlanDto ToDto(this SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        Price = plan.Price,
        PriceInCents = plan.PriceInCents,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        RequiresPaymentMethod = plan.RequiresPaymentMethod
    };

    public static SubscriptionDto ToDto(this Subscription subscription) => new()
    {
        Id = subscription.Id,
        CustomerReference = subscription.CustomerReference,
        State = subscription.State.ToString(),
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        PlanPrice = subscription.PlanPrice,
        PlanPriceInCents = subscription.PlanPriceInCents,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingDate = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
        CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod,
        ScheduledCancellationAt = subscription.ScheduledCancellationAt,
        ScheduledPlanHandle = subscription.ScheduledPlanHandle,
        AllowedActions = SubscriptionLifecyclePolicy.AllowedActions(subscription)
            .Select(action => action.ToString())
            .ToList()
    };

    public static UsageReportDto ToDto(this UsageReport report) => new()
    {
        SubscriptionId = report.SubscriptionId,
        ComponentHandle = report.ComponentHandle,
        UsageId = report.Record?.Id,
        RecordedQuantity = report.Record?.Quantity,
        Memo = report.Record?.Memo,
        RecordedAt = report.Record?.RecordedAt,
        PeriodToDateUnits = report.PeriodToDateUnits,
        UnitPriceInCents = report.UnitPriceInCents,
        EstimatedPeriodToDateCharge = report.EstimatedPeriodToDateCharge
    };

    public static PlanChangePreviewDto ToDto(this PlanChangePreview preview) => new()
    {
        SubscriptionId = preview.SubscriptionId,
        CurrentPlanHandle = preview.CurrentPlanHandle,
        TargetPlanHandle = preview.TargetPlanHandle,
        TargetPlanName = preview.TargetPlanName,
        Charge = preview.Charge,
        CreditApplied = preview.CreditApplied,
        PaymentDue = preview.PaymentDue,
        PaymentDueInCents = preview.PaymentDueInCents,
        ProratedAdjustment = preview.ProratedAdjustment,
        PreviewedAt = preview.PreviewedAt
    };
}
