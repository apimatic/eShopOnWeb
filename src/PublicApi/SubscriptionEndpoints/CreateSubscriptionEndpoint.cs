using System;
using System.Security.Claims;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated caller to a plan (POST /api/subscriptions). Idempotent: a customer
/// is created at most once per user and a live subscription to the same plan is never duplicated.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, HttpContext, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, HttpContext http, ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(request, http, subscriptionService);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, HttpContext http, ISubscriptionService subscriptionService)
    {
        var response = new CreateSubscriptionResponse(request?.CorrelationId() ?? Guid.NewGuid());

        var loginName = http.User.FindFirst(ClaimTypes.Name)?.Value;
        if (string.IsNullOrWhiteSpace(loginName))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request?.PlanHandle))
        {
            return Results.BadRequest(new ErrorDetails
            {
                StatusCode = StatusCodes.Status400BadRequest,
                Message = "A planHandle is required (the Maxio product handle of the plan to subscribe to)."
            });
        }

        var result = await subscriptionService.SubscribeAsync(loginName, request.PlanHandle, http.RequestAborted);

        response.Subscription = new SubscriptionSummaryDto
        {
            SubscriptionId = result.Subscription.Id,
            PlanHandle = result.Subscription.PlanHandle,
            PlanName = result.Subscription.PlanName,
            PriceAmount = result.Subscription.PriceAmount,
            Currency = result.Subscription.Currency,
            State = result.Subscription.State,
            NextBillingDate = result.Subscription.NextBillingDate
        };
        response.SubscriptionCreated = result.Created;

        // 201 when this call created the subscription; 200 when an existing one was returned.
        return result.Created
            ? Results.Created("/api/my-subscriptions", response)
            : Results.Ok(response);
    }
}
