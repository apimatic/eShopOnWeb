using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a plan, creating their billing customer if needed.
/// Repeating the request never creates a second customer or subscription.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, HttpContext, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, HttpContext httpContext, ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(request, httpContext, subscriptionService);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status202Accepted)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, HttpContext httpContext, ISubscriptionService subscriptionService)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.Content(new ErrorDetails { StatusCode = StatusCodes.Status400BadRequest, Message = "planHandle is required." }.ToString(),
                "application/json", statusCode: StatusCodes.Status400BadRequest);
        }

        var userName = httpContext.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Results.Unauthorized();
        }

        var result = await subscriptionService.SubscribeAsync(userName, request.PlanHandle.Trim(), httpContext.RequestAborted);
        response.Subscription = result.Subscription is null ? null : SubscriptionDto.From(result.Subscription);

        switch (result.Outcome)
        {
            case SubscribeOutcome.Created:
                response.Status = CreateSubscriptionResponse.StatusCreated;
                return Results.Created("api/my-subscriptions", response);

            case SubscribeOutcome.AlreadySubscribed:
                response.Status = CreateSubscriptionResponse.StatusAlreadySubscribed;
                response.Message = "You already have an active subscription to this plan.";
                return Results.Ok(response);

            default:
                response.Status = CreateSubscriptionResponse.StatusPending;
                response.Message = "The billing provider has not confirmed the subscription yet. Check GET /api/my-subscriptions shortly.";
                return Results.Accepted("api/my-subscriptions", response);
        }
    }
}
