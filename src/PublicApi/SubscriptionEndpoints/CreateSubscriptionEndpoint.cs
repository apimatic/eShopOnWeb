using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a plan. The caller's identity comes from the JWT;
/// the operation is idempotent per user and plan.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(request, subscriptionService);
            })
           .Produces<CreateSubscriptionResponse>()
           .ProducesProblem(401)
           .ProducesProblem(404)
           .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionService subscriptionService)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var userName = _httpContextAccessor.HttpContext?.User?.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["PlanHandle"] = new[] { "A subscription plan handle is required." }
            });
        }

        SubscribeResult result;
        try
        {
            result = await subscriptionService.SubscribeAsync(userName, request.PlanHandle);
        }
        catch (SubscriptionPlanNotFoundException)
        {
            return Results.Problem(
                title: "Plan not found",
                detail: $"No subscription plan with handle '{request.PlanHandle}' is available.",
                statusCode: 404);
        }

        response.Subscription = new SubscriptionDetailsDto
        {
            MaxioSubscriptionId = result.Subscription.MaxioSubscriptionId,
            State = result.Subscription.State,
            PlanHandle = result.Subscription.PlanHandle,
            PlanName = result.Subscription.PlanName,
            Price = result.Subscription.Price,
            PriceFormatted = result.Subscription.PriceFormatted,
            NextBillingDate = result.Subscription.NextBillingDate,
            CustomerReference = result.Subscription.CustomerReference,
            PaymentCollectionMethod = result.Subscription.PaymentCollectionMethod,
            ActivatedAt = result.Subscription.ActivatedAt,
            CreatedAt = result.Subscription.CreatedAt
        };

        return Results.Created($"api/my-subscriptions/{response.Subscription.MaxioSubscriptionId}", response);
    }
}