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

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan. Idempotent: ensures a single billing customer
/// exists for the user and reuses a live subscription to the same plan instead of creating a duplicate.
/// </summary>
public class SubscribeEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, ClaimsPrincipal user, ISubscriptionBillingService billing, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(request, user, billing, cancellationToken);
            })
           .Produces<SubscribeResponse>(StatusCodes.Status201Created)
           .Produces<SubscribeResponse>(StatusCodes.Status200OK)
           .ProducesProblem(StatusCodes.Status400BadRequest)
           .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, ClaimsPrincipal user, ISubscriptionBillingService billing, CancellationToken cancellationToken)
    {
        var userName = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Results.Unauthorized();
        }

        if (request is null || string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest("A planHandle is required. Choose one from GET /api/subscription-plans.");
        }

        var result = await billing.SubscribeAsync(userName, request.PlanHandle.Trim(), cancellationToken);

        var response = new SubscribeResponse
        {
            Subscription = result.Subscription.ToDto(),
            AlreadyActive = result.AlreadyActive,
            Message = result.AlreadyActive
                ? "You are already subscribed to this plan."
                : "Subscription created successfully.",
        };

        return result.AlreadyActive
            ? Results.Ok(response)
            : Results.Created("api/my-subscriptions", response);
    }
}
