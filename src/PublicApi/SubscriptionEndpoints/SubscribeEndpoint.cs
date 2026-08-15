using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// The hero flow: subscribes the authenticated caller to a plan. Ensures a Maxio customer exists for
/// the eShopOnWeb user (idempotent by reference), enrolls them, and returns the plan/price/state/
/// next-billing-date. A double-click never creates two customers or two subscriptions.
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, ResolvedSubscribeRequest, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, ISubscriptionBillingService billingService, ClaimsPrincipal user, UserManager<ApplicationUser> userManager, CancellationToken cancellationToken) =>
            {
                if (request is null || string.IsNullOrWhiteSpace(request.PlanHandle))
                {
                    return Results.BadRequest("A 'planHandle' is required. Fetch available handles from GET /api/subscription-plans.");
                }

                var subscriber = await CurrentSubscriber.ResolveAsync(user, userManager);
                if (subscriber is null)
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(
                    new ResolvedSubscribeRequest(subscriber, request.PlanHandle.Trim(), cancellationToken),
                    billingService);
            })
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .Produces<SubscribeResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ResolvedSubscribeRequest request, ISubscriptionBillingService billingService)
    {
        var result = await billingService.SubscribeAsync(request.Subscriber, request.PlanHandle, request.CancellationToken);

        var response = new SubscribeResponse(request.CorrelationId())
        {
            AlreadyExisted = result.AlreadyExisted,
            Subscription = result.Subscription.ToDto(),
        };

        // New subscription -> 201 Created; already-subscribed (idempotent no-op) -> 200 OK.
        return result.AlreadyExisted
            ? Results.Ok(response)
            : Results.Created($"api/my-subscriptions", response);
    }
}
