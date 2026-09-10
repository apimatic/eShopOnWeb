using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan. Ensures a Maxio customer exists for the user and
/// enrolls them. Idempotent: a repeated call (e.g. a double-click) never creates a second customer or
/// a second live subscription to the same plan — the existing subscription is returned. JWT-authenticated;
/// the subscriber is taken from the token, not the request body.
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, ClaimsPrincipal, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                SubscribeRequest request, ClaimsPrincipal user, IMaxioSubscriptionService subscriptionService,
                CancellationToken cancellationToken) =>
                await HandleAsync(request, user, subscriptionService, cancellationToken))
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .WithTags("SubscriptionEndpoints")
            .WithMetadata(new SwaggerOperationAttribute(
                summary: "Subscribe to a plan",
                description: "Enrolls the authenticated shopper in a subscription plan, creating their Maxio customer if needed."));
    }

    // Satisfies the IEndpoint contract; delegates to the cancellation-aware overload used by the route.
    public Task<IResult> HandleAsync(SubscribeRequest request, ClaimsPrincipal user, IMaxioSubscriptionService subscriptionService)
        => HandleAsync(request, user, subscriptionService, CancellationToken.None);

    public async Task<IResult> HandleAsync(SubscribeRequest request, ClaimsPrincipal user,
        IMaxioSubscriptionService subscriptionService, CancellationToken cancellationToken)
    {
        var subscriber = user.ToSubscriber();
        if (subscriber is null)
        {
            return Results.Unauthorized();
        }

        var subscription = await subscriptionService.SubscribeAsync(subscriber, request?.PlanHandle, cancellationToken);

        var response = new SubscribeResponse { Subscription = subscription.ToDto() };
        return Results.Created($"/api/my-subscriptions", response);
    }
}
