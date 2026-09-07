using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public static class CreateSubscriptionEndpoint
{
    public static async Task<IResult> HandleAsync(
        SubscribeRequest request,
        MaxioService maxioService,
        HttpContext httpContext)
    {
        var userId = GetUserIdFromContext(httpContext);

        try
        {
            var subscriptionInfo = await maxioService.CreateOrGetSubscriptionAsync(
                userId,
                request.UserEmail,
                request.UserFirstName,
                request.UserLastName,
                request.ProductHandle);

            if (subscriptionInfo == null)
                return Results.BadRequest(new { error = "Failed to create subscription" });

            return Results.Created(
                $"/api/subscriptions/{subscriptionInfo.SubscriptionId}",
                new SubscribeResponse
                {
                    SubscriptionId = subscriptionInfo.SubscriptionId,
                    State = subscriptionInfo.State,
                    CurrentPeriodEndsAt = subscriptionInfo.CurrentPeriodEndsAt,
                    ProductHandle = subscriptionInfo.ProductHandle
                });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static int GetUserIdFromContext(HttpContext httpContext)
    {
        var userIdClaim = httpContext.User?.FindFirst("sub")?.Value
            ?? httpContext.User?.FindFirst("nameid")?.Value;

        if (int.TryParse(userIdClaim, out var userId))
            return userId;

        throw new InvalidOperationException("User ID not found in token claims");
    }
}

public record SubscribeRequest
{
    public string UserEmail { get; init; } = string.Empty;
    public string UserFirstName { get; init; } = string.Empty;
    public string UserLastName { get; init; } = string.Empty;
    public string ProductHandle { get; init; } = string.Empty;
}

public record SubscribeResponse
{
    public int SubscriptionId { get; init; }
    public string State { get; init; } = string.Empty;
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
    public string ProductHandle { get; init; } = string.Empty;
}
