using System;
using System.Security.Claims;
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
/// Subscribes the authenticated caller to a plan. Ensures a single billing customer exists for the user
/// and is idempotent: repeat calls (e.g. a double-click) return the existing subscription rather than
/// creating a duplicate. Responds 201 when a new subscription is created, 200 when one already existed.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ClaimsPrincipal, ISubscriptionManagementService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionManagementService subscriptionService) =>
            {
                return await HandleAsync(request, user, subscriptionService);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionManagementService subscriptionService)
    {
        var subscriber = SubscriberIdentityFactory.FromPrincipal(user, request.FirstName, request.LastName);
        if (subscriber is null)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.Problem(
                detail: "planHandle is required. Call GET /api/subscription-plans to see available plan handles.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Missing plan handle");
        }

        var response = new CreateSubscriptionResponse(request.CorrelationId());
        try
        {
            var result = await subscriptionService.SubscribeAsync(subscriber, request.PlanHandle);
            response.Subscription = result.Subscription.ToDto();
            response.AlreadyExisted = result.AlreadyExisted;

            return result.AlreadyExisted
                ? Results.Ok(response)
                : Results.Created("api/my-subscriptions", response);
        }
        catch (Exception ex) when (SubscriptionMapping.TryToProblem(ex) is IResult problem)
        {
            return problem;
        }
    }
}
