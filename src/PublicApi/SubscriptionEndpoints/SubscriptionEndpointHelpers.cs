using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Shared mapping helpers for the subscription endpoints.
/// </summary>
internal static class SubscriptionEndpointHelpers
{
    /// <summary>
    /// Converts a billing failure into a problem response. Provider 4xx rejections keep their
    /// status; provider unreachability and unknown outcomes become 502 so callers do not retry
    /// a deterministic rejection as if it were transient, or vice versa.
    /// </summary>
    public static IResult ToProblemResult(this MaxioBillingException ex)
    {
        var statusCode = ex.StatusCode is >= 400 and < 500
            ? ex.StatusCode.Value
            : StatusCodes.Status502BadGateway;

        return Results.Problem(
            statusCode: statusCode,
            title: statusCode == StatusCodes.Status502BadGateway ? "Billing provider error" : "Subscription request rejected",
            detail: ex.Message);
    }

    public static SubscriptionDto ToDto(this SubscriptionSummary summary) =>
        new()
        {
            SubscriptionId = summary.SubscriptionId,
            PlanHandle = summary.PlanHandle,
            PlanName = summary.PlanName,
            Price = summary.Price,
            State = summary.State,
            NextBillingDate = summary.NextBillingDate,
            CurrentPeriodEndsAt = summary.CurrentPeriodEndsAt,
            StartedAt = summary.StartedAt,
            Reference = summary.Reference
        };
}
