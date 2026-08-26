using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionEndpointHelpers
{
    public static string? GetUserId(ClaimsPrincipal claimsPrincipal)
        => claimsPrincipal.FindFirst(ClaimTypes.Name)?.Value ?? claimsPrincipal.Identity?.Name;

    public static SubscriptionDto ToDto(CustomerSubscription subscription) => new()
    {
        Id = subscription.Id,
        ProductHandle = subscription.ProductHandle,
        ProductName = subscription.ProductName,
        State = subscription.State,
        PriceInCents = subscription.PriceInCents,
        NextBillingAt = subscription.NextBillingAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt
    };

    /// <summary>
    /// Maps a billing-boundary failure to an HTTP result: a provider 4xx surfaces as the same
    /// client-actionable 4xx; anything without a meaningful provider status is a 502.
    /// </summary>
    public static IResult ToProblem(BillingException exception)
    {
        var statusCode = exception.ProviderStatusCode is { } status && (int)status >= 400 && (int)status < 500
            ? (int)status
            : StatusCodes.Status502BadGateway;

        return Results.Problem(
            detail: exception.Message,
            statusCode: statusCode,
            title: "Billing error");
    }
}
