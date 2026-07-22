using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Applies a pause, resume, cancel or reactivate transition to a subscription (UC4)
/// </summary>
public class LifecycleEndpoint : IEndpoint<IResult, LifecycleRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/lifecycle",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, LifecycleRequest request, ISubscriptionService subscriptionService) =>
            {
                request.SubscriptionId = subscriptionId;
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<LifecycleResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(LifecycleRequest request, ISubscriptionService subscriptionService)
    {
        var response = new LifecycleResponse(request.CorrelationId());

        var subscription = await subscriptionService.ApplyLifecycleActionAsync(
            request.SubscriptionId, request.Action, request.Timing, request.Reason);

        response.Action = request.Action;
        response.State = subscription.State;
        response.Subscription = subscription.ToDto();
        response.EffectiveAt = request.Action == SubscriptionLifecycleAction.Cancel && request.Timing == CancellationTiming.EndOfPeriod
            ? subscription.CurrentPeriodEndsAt
            : DateTimeOffset.UtcNow;

        return Results.Ok(response);
    }
}
