using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated caller to a plan. Idempotent: a double-click does not create a
/// second Maxio customer or a second subscription to the same plan.
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest>
{
    private readonly ISubscriptionBillingService _billing;

    public SubscribeEndpoint(ISubscriptionBillingService billing)
    {
        _billing = billing;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (SubscribeRequest request, ClaimsPrincipal user, CancellationToken ct) =>
                await HandleAsync(request, user, ct))
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .Produces<SubscribeResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    // Interface member — the route supplies the principal and cancellation token via the overload below.
    public Task<IResult> HandleAsync(SubscribeRequest request) =>
        Task.FromResult(Results.Problem("This endpoint requires an authenticated caller.", statusCode: StatusCodes.Status401Unauthorized));

    public async Task<IResult> HandleAsync(SubscribeRequest request, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var subscriber = SubscriberIdentityFactory.FromPrincipal(user);
        if (subscriber is null)
            return Results.Unauthorized();

        if (request is null || string.IsNullOrWhiteSpace(request.PlanHandle))
            return Results.Problem(
                detail: "A 'planHandle' is required. Choose one from GET /api/subscription-plans.",
                statusCode: StatusCodes.Status400BadRequest);

        try
        {
            var result = await _billing.SubscribeAsync(subscriber, request.PlanHandle.Trim(), cancellationToken);
            var response = new SubscribeResponse
            {
                Subscription = result.Subscription,
                AlreadyExisted = result.AlreadyExisted,
                Message = result.AlreadyExisted
                    ? "You are already subscribed to this plan."
                    : "Subscription created."
            };

            return result.AlreadyExisted
                ? Results.Ok(response)
                : Results.Created($"api/my-subscriptions", response);
        }
        catch (SubscriptionBillingException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: ex.SuggestedStatusCode);
        }
    }
}
