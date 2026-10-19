using System;
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
/// Subscribes the calling user to a plan. Idempotent per user: repeating the request (e.g. a double-click)
/// never creates a second Maxio customer or subscription.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, HttpContext, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, HttpContext httpContext, ISubscriptionBillingService billingService) =>
            {
                return await HandleAsync(request, httpContext, billingService);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status202Accepted)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, HttpContext httpContext, ISubscriptionBillingService billingService)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var subscriber = await SubscriberResolver.ResolveAsync(httpContext);
        if (subscriber is null)
        {
            return Results.Unauthorized();
        }

        var result = await billingService.SubscribeAsync(subscriber, request.PlanHandle, httpContext.RequestAborted);
        response.Subscription = result.Subscription is null ? null : SubscriptionDto.From(result.Subscription);

        switch (result.Outcome)
        {
            case SubscribeOutcome.Created:
                response.Status = "created";
                return Results.Created("api/my-subscriptions", response);
            case SubscribeOutcome.AlreadySubscribed:
                response.Status = "already_subscribed";
                response.Message = "You are already subscribed to this plan.";
                return Results.Ok(response);
            case SubscribeOutcome.PendingConfirmation:
                response.Status = "pending_confirmation";
                response.Message = "Your subscription request was sent but could not be confirmed yet. Check GET /api/my-subscriptions shortly; repeating this request is safe.";
                return Results.Accepted("api/my-subscriptions", response);
            default:
                throw new InvalidOperationException($"Unhandled subscribe outcome {result.Outcome}.");
        }
    }
}
