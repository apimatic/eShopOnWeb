using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan. Idempotent: ensures a single Maxio customer
/// exists for the shopper and will not create a duplicate subscription for a plan they are
/// already actively subscribed to (a double-submit returns the existing subscription).
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, HttpContext, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateSubscriptionRequest request, HttpContext http, ISubscriptionService subscriptionService) =>
                await HandleAsync(request, http, subscriptionService))
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, HttpContext http, ISubscriptionService subscriptionService)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.Problem(
                title: "Invalid subscription request",
                detail: "A 'planHandle' is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            var subscription = await subscriptionService.SubscribeAsync(http.User, request.PlanHandle, http.RequestAborted);
            var response = new CreateSubscriptionResponse(request.CorrelationId()) { Subscription = subscription };

            // A pre-existing subscription (idempotent replay) is signalled with 200; a freshly
            // created one with 201.
            return subscription.AlreadyExisted
                ? Results.Ok(response)
                : Results.Created($"api/subscriptions/{subscription.Id}", response);
        }
        catch (Exception ex)
        {
            return SubscriptionProblemMapper.ToProblem(ex);
        }
    }
}
