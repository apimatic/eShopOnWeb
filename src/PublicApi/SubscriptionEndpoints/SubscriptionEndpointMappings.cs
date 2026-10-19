using System;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.PublicApi.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Shared mapping for the subscription endpoints: DTOs, caller identity and the one failure → HTTP status ladder.</summary>
public static class SubscriptionEndpointMappings
{
    public const string TAG = "SubscriptionEndpoints";

    /// <summary>The caller's eShopOnWeb user name, taken from the JWT.</summary>
    public static string? UserName(ClaimsPrincipal user) =>
        user.Identity?.Name ?? user.FindFirst(ClaimTypes.Name)?.Value ?? user.FindFirst("unique_name")?.Value;

    public static SubscriptionPlanDto ToDto(SubscriptionPlan plan) => new()
    {
        Id = plan.Id,
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        Price = plan.Price,
        PriceInCents = plan.PriceInCents,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        RequiresPaymentMethod = plan.RequiresPaymentMethod
    };

    public static SubscriptionDto ToDto(BillingSubscription s) => new()
    {
        Id = s.Id,
        PlanHandle = s.PlanHandle,
        PlanName = s.PlanName,
        State = s.State,
        Price = s.Price,
        PriceInCents = s.PriceInCents,
        Currency = s.Currency,
        Interval = s.Interval,
        IntervalUnit = s.IntervalUnit,
        NextBillingAt = s.NextBillingAt,
        CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
        ActivatedAt = s.ActivatedAt,
        CreatedAt = s.CreatedAt
    };

    /// <summary>
    /// Caller-fixable failures keep a 4xx; provider outages, our credentials and throttling become 5xx.
    /// Only the caller-safe message leaves the process.
    /// </summary>
    public static IResult ToResult(SubscriptionBillingException ex, Guid correlationId)
    {
        var status = ex.Kind switch
        {
            BillingFailureKind.InvalidRequest => ex.ProviderStatus == StatusCodes.Status422UnprocessableEntity
                ? StatusCodes.Status422UnprocessableEntity
                : StatusCodes.Status400BadRequest,
            BillingFailureKind.Conflict => StatusCodes.Status409Conflict,
            _ when ex.ProviderStatus == StatusCodes.Status429TooManyRequests => StatusCodes.Status503ServiceUnavailable,
            _ when ex.ProviderStatus == null && ex.InnerException is MaxioAdvancedBilling.Core.Exceptions.SdkTimeoutException or OperationCanceledException
                => StatusCodes.Status504GatewayTimeout,
            _ => StatusCodes.Status502BadGateway
        };

        var body = new SubscriptionErrorResponse(correlationId)
        {
            StatusCode = status,
            Message = ex.Message,
            Errors = ex.Errors.ToList(),
            ExistingSubscription = ex is AlreadySubscribedException already ? ToDto(already.Existing) : null
        };
        return Results.Json(body, statusCode: status);
    }
}
