using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Shared translation and guard logic for the subscription endpoints: domain types to DTOs,
/// domain failures to HTTP results, and the ownership check that keeps one customer out of
/// another's subscription.
/// </summary>
public static class SubscriptionEndpointSupport
{
    /// <summary>
    /// Runs an endpoint body, turning the domain's typed failures into the matching HTTP result.
    /// Anything unrecognised is left to propagate to the host's exception middleware.
    /// </summary>
    public static async Task<IResult> ExecuteAsync(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (InvalidSubscriptionOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (BillingConfigurationException ex)
        {
            // The deployment's configuration and the provider's catalog disagree; this is an
            // operator problem, not something the caller can correct by retrying.
            return Results.Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Billing configuration error");
        }
        catch (BillingProviderNotFoundException ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }
        catch (BillingProviderValidationException ex)
        {
            return Results.BadRequest(new { error = ex.Message, errors = ex.Errors });
        }
        catch (BillingProviderUnavailableException ex)
        {
            return Results.Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Billing provider unavailable");
        }
        catch (BillingProviderException ex)
        {
            return Results.Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status502BadGateway,
                title: "Billing provider error");
        }
    }

    /// <summary>
    /// Confirms the caller may act on <paramref name="subscriptionId"/>. Administrators may act on
    /// any subscription; every other caller may act only on their own. Returns null when allowed,
    /// otherwise the result to return instead.
    /// </summary>
    /// <remarks>
    /// A subscription the caller does not own is reported as not found rather than forbidden, so
    /// the endpoint does not leak which subscription ids exist.
    /// </remarks>
    public static async Task<IResult?> EnsureCallerMayActOnAsync(
        ClaimsPrincipal user,
        int subscriptionId,
        ISubscriptionService subscriptionService,
        CancellationToken cancellationToken)
    {
        if (user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS))
        {
            return null;
        }

        var userName = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Results.Unauthorized();
        }

        var owned = await subscriptionService.ListSubscriptionsAsync(userName, cancellationToken);
        if (owned.Any(s => s.Id == subscriptionId))
        {
            return null;
        }

        return Results.NotFound(new { error = $"No subscription with id {subscriptionId} belongs to this account." });
    }

    public static SubscriptionPlanDto ToDto(SubscriptionPlan plan) => new()
    {
        Id = plan.Id,
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description ?? string.Empty,
        Price = plan.Price,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        RequiresPaymentMethod = plan.RequiresPaymentMethod
    };

    public static SubscriptionDto ToDto(Subscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State.ToString(),
        IsActive = subscription.IsActive,
        PlanHandle = subscription.PlanHandle ?? string.Empty,
        PlanName = subscription.PlanName ?? string.Empty,
        PlanPrice = subscription.PlanPrice,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextAssessmentAt = subscription.NextAssessmentAt,
        CanceledAt = subscription.CanceledAt,
        CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod,
        DelayedCancelAt = subscription.DelayedCancelAt,
        PendingPlanHandle = subscription.PendingPlanHandle ?? string.Empty,
        LegalActions = subscription.LegalActions.Select(a => a.ToString()).ToArray()
    };

    public static PlanChangePreviewDto ToDto(PlanChangePreview preview) => new()
    {
        SubscriptionId = preview.SubscriptionId,
        CurrentPlanHandle = preview.CurrentPlanHandle,
        TargetPlanHandle = preview.TargetPlanHandle,
        Timing = preview.Timing.ToString(),
        ProratedAdjustment = preview.ProratedAdjustment,
        Charge = preview.Charge,
        PaymentDue = preview.PaymentDue,
        CreditApplied = preview.CreditApplied,
        EffectiveAt = preview.EffectiveAt,
        PreviewToken = preview.Token
    };

    public static UsageReceiptDto ToDto(UsageReceipt receipt) => new()
    {
        UsageId = receipt.Recorded.Id,
        SubscriptionId = receipt.Recorded.SubscriptionId,
        ComponentId = receipt.Recorded.ComponentId,
        ComponentHandle = receipt.Recorded.ComponentHandle ?? string.Empty,
        Quantity = receipt.Recorded.Quantity,
        Memo = receipt.Recorded.Memo ?? string.Empty,
        RecordedAt = receipt.Recorded.RecordedAt,
        PeriodToDateUnits = receipt.PeriodToDateUnits,
        PeriodToDateAvailable = receipt.PeriodToDateAvailable,
        BillingNote = "Recorded usage is billed on the next renewal invoice."
    };

    /// <summary>
    /// Parses a caller-supplied plan-change timing. Defaults to applying the change immediately
    /// with proration when nothing is supplied.
    /// </summary>
    public static PlanChangeTiming ParseTiming(string? timing)
    {
        if (string.IsNullOrWhiteSpace(timing))
        {
            return PlanChangeTiming.Immediate;
        }

        if (Enum.TryParse<PlanChangeTiming>(timing, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        throw new InvalidSubscriptionOperationException(
            $"'{timing}' is not a valid plan-change timing. Use 'Immediate' or 'NextRenewal'.");
    }

    /// <summary>Parses a caller-supplied lifecycle action.</summary>
    public static SubscriptionLifecycleAction ParseAction(string? action)
    {
        if (Enum.TryParse<SubscriptionLifecycleAction>(action, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        var legal = string.Join(", ", Enum.GetNames<SubscriptionLifecycleAction>());
        throw new InvalidSubscriptionOperationException(
            $"'{action}' is not a valid lifecycle action. Valid actions: {legal}.");
    }
}
