using System.Security.Claims;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Projects subscription domain types onto the API's wire contract, and resolves who the caller is
/// allowed to act as.
/// </summary>
internal static class SubscriptionDtoMapper
{
    internal static SubscriptionPlanDto ToDto(this BillingPlan plan) => new()
    {
        Id = plan.Id,
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description ?? string.Empty,
        Price = plan.Price,
        IntervalLength = plan.IntervalLength,
        IntervalUnit = plan.IntervalUnit,
        PriceDescription = plan.PriceDescription,
        RequiresPaymentMethod = plan.RequiresPaymentMethod
    };

    internal static SubscriptionDto ToDto(this Subscription subscription) => new()
    {
        Id = subscription.Id,
        UserReference = subscription.UserReference,
        CustomerId = subscription.CustomerId,
        Plan = subscription.Plan.ToDto(),
        State = subscription.State.ToString(),
        ProviderState = subscription.ProviderState,
        ActivatedAt = subscription.ActivatedAt,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextAssessmentAt = subscription.NextAssessmentAt,
        DelayedCancelAt = subscription.DelayedCancelAt,
        CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod,
        Balance = subscription.Balance,
        PendingPlanHandle = subscription.PendingPlanHandle ?? string.Empty,
        CanPause = subscription.CanPause,
        CanResume = subscription.CanResume,
        CanCancel = subscription.CanCancel,
        CanReactivate = subscription.CanReactivate,
        CanChangePlan = subscription.CanChangePlan,
        CanRecordUsage = subscription.CanRecordUsage
    };

    internal static UsageReportDto ToDto(this UsageReport report) => new()
    {
        UsageId = report.Recorded.Id,
        Quantity = report.Recorded.Quantity,
        Memo = report.Recorded.Memo ?? string.Empty,
        RecordedAt = report.Recorded.RecordedAt,
        IsTotalAvailable = report.IsTotalAvailable,
        PeriodToDateQuantity = report.PeriodToDateQuantity,
        PeriodToDateCharge = report.PeriodToDateCharge,
        TotalUnavailableReason = report.TotalUnavailableReason ?? string.Empty
    };

    internal static PlanChangePreviewDto ToDto(this PlanChangePreview preview) => new()
    {
        SubscriptionId = preview.SubscriptionId,
        CurrentPlan = preview.CurrentPlan.ToDto(),
        TargetPlan = preview.TargetPlan.ToDto(),
        Timing = preview.Timing.ToString(),
        ProratedCharge = preview.ProratedCharge,
        ProratedCredit = preview.ProratedCredit,
        NetAmount = preview.NetAmount,
        AmountDueNow = preview.AmountDueNow,
        EffectiveAt = preview.EffectiveAt,
        Fingerprint = preview.Fingerprint
    };

    /// <summary>
    /// The authenticated caller's identity. Taken from the bearer token, never from the request
    /// body, so a caller cannot name someone else.
    /// </summary>
    internal static string RequireUserReference(this ClaimsPrincipal user)
    {
        Guard.Against.Null(user.Identity?.Name, nameof(user.Identity.Name));
        return user.Identity!.Name!;
    }

    /// <summary>
    /// Which subscriptions the caller may act on: their own, or — for an administrator — any.
    /// <c>null</c> means unrestricted, matching <c>ISubscriptionService</c>'s convention.
    /// </summary>
    internal static string? ResolveActingScope(this ClaimsPrincipal user) =>
        user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS)
            ? null
            : user.RequireUserReference();
}
