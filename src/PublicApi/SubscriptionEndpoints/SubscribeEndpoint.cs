using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using AppSubscribeRequest = Microsoft.eShopWeb.ApplicationCore.Subscriptions.SubscribeRequest;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan. Idempotent: ensures a single billing
/// customer exists and never creates a duplicate live subscription to the same plan.
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, ClaimsPrincipal, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(request, user, subscriptionService);
            })
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .Produces<SubscribeResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, ClaimsPrincipal user,
        ISubscriptionService subscriptionService)
    {
        var response = new SubscribeResponse(request.CorrelationId());

        // Identity is sourced exclusively from the authenticated token, never from the payload.
        var userReference = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userReference))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest("A planHandle is required. Call GET /api/subscription-plans to see the available plans.");
        }

        var appRequest = new AppSubscribeRequest(
            userReference: userReference,
            email: userReference,
            planHandle: request.PlanHandle,
            firstName: request.FirstName,
            lastName: request.LastName);

        var result = await subscriptionService.SubscribeAsync(appRequest);

        response.Subscription = SubscriptionDtoMapper.ToDto(result.Subscription);
        response.AlreadySubscribed = result.AlreadySubscribed;
        response.Message = result.AlreadySubscribed
            ? $"You are already subscribed to {response.Subscription.PlanName}."
            : $"You are now subscribed to {response.Subscription.PlanName}.";

        return result.AlreadySubscribed
            ? Results.Ok(response)
            : Results.Created($"api/my-subscriptions", response);
    }
}
