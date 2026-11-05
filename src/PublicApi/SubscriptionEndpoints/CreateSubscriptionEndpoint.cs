using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan (idempotent per user and plan)
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, SubscriberContext context, CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(request.PlanHandle))
                    return Results.BadRequest(new { message = "planHandle is required." });

                var subscriber = await context.ResolveAsync(user);
                if (subscriber is null)
                    return Results.Unauthorized();

                var result = await context.Billing.SubscribeAsync(subscriber, request.PlanHandle.Trim(), cancellationToken);
                var response = new CreateSubscriptionResponse(request.CorrelationId())
                {
                    Subscription = result.Subscription.ToDto(),
                    Created = result.Created,
                };
                return result.Created
                    ? Results.Created("api/my-subscriptions", response)
                    : Results.Ok(response);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .WithTags("SubscriptionEndpoints");
    }
}
