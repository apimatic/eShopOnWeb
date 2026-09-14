using System;
using System.Linq;
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
/// Subscribes the authenticated shopper to a subscription plan.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<ErrorDetails>(StatusCodes.Status400BadRequest)
            .Produces<ErrorDetails>(StatusCodes.Status404NotFound)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionService subscriptionService)
    {
        var planHandle = (request?.PlanHandle ?? string.Empty).Trim();
        if (planHandle.Length == 0)
        {
            throw new ApiException(StatusCodes.Status400BadRequest, "PlanHandle is required.");
        }

        var plans = await subscriptionService.ListPlansAsync(default);
        if (!plans.Any(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ApiException(StatusCodes.Status404NotFound, $"No subscription plan with handle '{planHandle}' was found.");
        }

        var result = await subscriptionService.SubscribeAsync(planHandle, default);
        var response = new CreateSubscriptionResponse(request!.CorrelationId())
        {
            Subscription = SubscriptionDto.From(result.Subscription),
            AlreadySubscribed = result.AlreadySubscribed
        };

        return result.AlreadySubscribed
            ? Results.Ok(response)
            : Results.Created($"api/subscriptions/{response.Subscription.Id}", response);
    }
}
