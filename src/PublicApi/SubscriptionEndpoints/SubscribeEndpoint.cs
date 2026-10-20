using Microsoft.AspNetCore.Builder;
using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a plan (idempotent per user and plan)
/// </summary>
public class SubscribeEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, ClaimsPrincipal principal, UserManager<ApplicationUser> userManager,
                ISubscriptionService subscriptions, CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(request.PlanHandle))
                    return Results.BadRequest(new { message = "planHandle is required." });

                var subscriber = await SubscriberResolver.ResolveAsync(principal, userManager);
                if (subscriber == null) return Results.Unauthorized();

                var result = await subscriptions.SubscribeAsync(subscriber.UserId, subscriber.Email, request.PlanHandle.Trim(), ct);
                var response = new SubscribeResponse(request.CorrelationId())
                {
                    Subscription = result.Subscription,
                    AlreadySubscribed = result.AlreadySubscribed
                };
                return result.AlreadySubscribed
                    ? Results.Ok(response)
                    : Results.Created("api/my-subscriptions", response);
            })
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .Produces<SubscribeResponse>(StatusCodes.Status200OK)
            .WithTags("SubscriptionEndpoints");
    }
}
