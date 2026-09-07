using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest
{
    public string? ProductHandle { get; set; }
}

public static class CreateSubscriptionEndpoint
{
    public static void MapCreateSubscription(this IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (CreateSubscriptionRequest request, ClaimsPrincipal user, IMaxioSubscriptionService subscriptionService, CancellationToken ct) =>
            {
                var userIdClaim = user.FindFirst("sub") ?? user.FindFirst(ClaimTypes.NameIdentifier);
                if (userIdClaim is null)
                    return Results.Unauthorized();

                var userId = userIdClaim.Value;
                var emailClaim = user.FindFirst(ClaimTypes.Email) ?? user.FindFirst("email");
                var email = emailClaim?.Value ?? userId;

                if (string.IsNullOrEmpty(request.ProductHandle))
                    return Results.BadRequest(new { error = "ProductHandle is required" });

                try
                {
                    var subscription = await subscriptionService.CreateSubscriptionAsync(userId, request.ProductHandle, ct);

                    return Results.Ok(new
                    {
                        subscription = subscription.Subscription
                    });
                }
                catch
                {
                    return Results.StatusCode(StatusCodes.Status500InternalServerError);
                }
            })
            .WithName("CreateSubscription")
            .RequireAuthorization()
            .Produces<object>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints")
            .WithSummary("Create a subscription")
            .WithDescription("Subscribe to a plan");
    }
}
