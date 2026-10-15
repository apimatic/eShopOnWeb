using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionResults
{
    public static SubscriptionDto ToDto(SubscriptionSummary s) => new()
    {
        SubscriptionId = s.SubscriptionId,
        PlanHandle = s.PlanHandle,
        PlanName = s.PlanName,
        Price = s.PriceInCents / 100m,
        PriceInCents = s.PriceInCents,
        State = s.State,
        NextBillingAt = s.NextBillingAt,
        CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
        CreatedAt = s.CreatedAt
    };

    /// <summary>The caller identity comes from the JWT name claim; null when absent.</summary>
    public static string? UserName(ClaimsPrincipal user) => user.Identity?.Name;

    /// <summary>One mapping from failure kind to HTTP outcome, shared by every subscription endpoint.</summary>
    public static IResult Failure(SubscriptionException ex) => ex.Failure switch
    {
        SubscriptionFailure.PlanNotFound => Results.Problem(ex.Message, statusCode: StatusCodes.Status404NotFound),
        SubscriptionFailure.InProgress => Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict),
        SubscriptionFailure.Rejected => Results.Problem(ex.Message, statusCode: StatusCodes.Status422UnprocessableEntity),
        _ => Results.Problem("Billing is temporarily unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable)
    };
}
