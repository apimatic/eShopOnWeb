using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Shared helpers for the subscription endpoints: extracting the caller identity from the JWT and
/// translating billing exceptions into appropriate HTTP responses.
/// </summary>
internal static class SubscriptionEndpointHelpers
{
    /// <summary>Returns the authenticated user's name (the token's identity), or null.</summary>
    public static string? GetUserName(ClaimsPrincipal user)
        => user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;

    /// <summary>Maps a domain subscription to its API DTO.</summary>
    public static SubscriptionDto ToDto(CustomerSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        PriceInCents = subscription.PriceInCents,
        FormattedPrice = subscription.FormattedPrice,
        Currency = subscription.Currency,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingAt = subscription.NextBillingAt,
        CustomerReference = subscription.CustomerReference,
    };

    /// <summary>Maps a billing exception to a ProblemDetails response with a sensible status code.</summary>
    public static IResult ToProblem(SubscriptionBillingException exception)
    {
        return exception switch
        {
            BillingNotConfiguredException => Results.Problem(
                detail: exception.Message,
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Subscription billing is not configured"),

            SubscriptionPlanNotFoundException => Results.Problem(
                detail: exception.Message,
                statusCode: StatusCodes.Status400BadRequest,
                title: "Unknown subscription plan"),

            _ => Results.Problem(
                detail: exception.Message,
                statusCode: MapUpstreamStatus(exception.UpstreamStatusCode),
                title: "Billing provider error"),
        };
    }

    private static int MapUpstreamStatus(int? upstreamStatusCode) => upstreamStatusCode switch
    {
        null => StatusCodes.Status502BadGateway,
        429 => StatusCodes.Status429TooManyRequests,
        503 => StatusCodes.Status503ServiceUnavailable,
        >= 500 => StatusCodes.Status502BadGateway,
        >= 400 => StatusCodes.Status400BadRequest,
        _ => StatusCodes.Status502BadGateway,
    };
}
