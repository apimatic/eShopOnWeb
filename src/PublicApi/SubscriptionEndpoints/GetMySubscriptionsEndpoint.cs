using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public static class GetMySubscriptionsEndpoint
{
    public static void MapGetMySubscriptions(this IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (ClaimsPrincipal user, IMaxioSubscriptionService subscriptionService, CancellationToken ct) =>
            {
                var userIdClaim = user.FindFirst("sub") ?? user.FindFirst(ClaimTypes.NameIdentifier);
                if (userIdClaim is null)
                    return Results.Unauthorized();

                var userId = userIdClaim.Value;

                try
                {
                    var subscriptions = await subscriptionService.GetUserSubscriptionsAsync(userId, ct);

                    return Results.Ok(new
                    {
                        subscriptions = subscriptions.ToList()
                    });
                }
                catch
                {
                    return Results.StatusCode(StatusCodes.Status500InternalServerError);
                }
            })
            .WithName("GetMySubscriptions")
            .RequireAuthorization()
            .Produces<object>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints")
            .WithSummary("Get my subscriptions")
            .WithDescription("Retrieve all subscriptions for the authenticated user");
    }
}
