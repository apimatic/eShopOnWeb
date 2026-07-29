using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan. Ensures a Maxio customer exists for the user
/// (idempotent) and enrols them, returning the confirmed plan, price, state and next billing date.
/// A repeated request (e.g. a double-click) returns the existing subscription rather than creating
/// a duplicate.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, SubscribeRequest, ClaimsPrincipal>
{
    private readonly IMaxioSubscriptionService _subscriptionService;

    public CreateSubscriptionEndpoint(IMaxioSubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, ClaimsPrincipal user) =>
            {
                return await HandleAsync(request, user);
            })
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .Produces<SubscribeResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, ClaimsPrincipal user)
    {
        var subscriber = user.ToSubscriber();
        if (subscriber is null)
        {
            return Results.Problem(
                detail: "The authenticated token does not carry a usable user identity.",
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized");
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.Problem(
                detail: "planHandle is required.",
                statusCode: StatusCodes.Status422UnprocessableEntity,
                title: "Invalid Subscription Request");
        }

        var result = await _subscriptionService.SubscribeAsync(subscriber, request.PlanHandle.Trim());
        if (!result.IsSuccess)
        {
            return result.ToProblem();
        }

        var subscription = result.Value;
        var response = new SubscribeResponse(request.CorrelationId())
        {
            Subscription = subscription.ToDto(),
            AlreadyExisted = subscription.AlreadyExisted,
            Message = subscription.AlreadyExisted
                ? $"Already subscribed to '{subscription.PlanName ?? subscription.PlanHandle}' (subscription {subscription.Id}, state '{subscription.State}')."
                : $"Subscribed to '{subscription.PlanName ?? subscription.PlanHandle}' (subscription {subscription.Id}, state '{subscription.State}')."
        };

        // A brand-new enrolment is a resource creation (201); an idempotent hit is a plain 200.
        return subscription.AlreadyExisted
            ? Results.Ok(response)
            : Results.Created($"api/subscriptions/{subscription.Id}", response);
    }
}
