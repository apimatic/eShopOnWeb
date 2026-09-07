using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public static class GetMySubscriptionsEndpoint
{
    public static async Task<IResult> HandleAsync(MaxioService maxioService, HttpContext httpContext)
    {
        var userId = GetUserIdFromContext(httpContext);

        try
        {
            var userSubs = maxioService.GetUserSubscriptions(userId);
            var subscriptions = new List<SubscriptionInfo>();

            foreach (var sub in userSubs)
            {
                var subInfo = await maxioService.GetSubscriptionAsync(sub.SubscriptionId);
                if (subInfo != null)
                    subscriptions.Add(subInfo);
            }

            return Results.Ok(new GetMySubscriptionsResponse
            {
                Subscriptions = subscriptions
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

public record GetMySubscriptionsResponse
{
    public List<SubscriptionInfo> Subscriptions { get; init; } = new();
}
