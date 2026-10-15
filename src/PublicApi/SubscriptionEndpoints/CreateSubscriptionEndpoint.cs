using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan. Idempotent per shopper: repeating the request
/// returns the existing subscription instead of creating another.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionService, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ISubscriptionService subscriptionService, HttpContext context) =>
            {
                return await HandleAsync(request, subscriptionService, context);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status202Accepted)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionService subscriptionService, HttpContext context)
    {
        var userName = context.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Results.Unauthorized();
        }

        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var result = await subscriptionService.SubscribeAsync(userName, request.PlanHandle, context.RequestAborted);

        response.PlanHandle = result.PlanHandle;
        response.Subscription = result.Subscription?.ToDto();
        switch (result.Outcome)
        {
            case SubscribeOutcome.Created:
                response.Status = "created";
                return Results.Created("api/my-subscriptions", response);
            case SubscribeOutcome.AlreadySubscribed:
                response.Status = "already_subscribed";
                return Results.Ok(response);
            case SubscribeOutcome.Pending:
                response.Status = "pending";
                return Results.Accepted("api/my-subscriptions", response);
            default:
                throw new InvalidOperationException($"Unexpected subscribe outcome {result.Outcome}.");
        }
    }
}
