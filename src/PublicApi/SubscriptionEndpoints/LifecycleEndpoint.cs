using System;
using System.Security.Claims;
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
/// [Authorize] One management surface for the four UC4 lifecycle actions (pause/resume/cancel/
/// reactivate): against the caller's own subscription by default, or an explicit subscription when
/// the caller is an Administrator ("any").
/// </summary>
public class LifecycleEndpoint : IEndpoint<IResult, LifecycleRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/lifecycle",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (LifecycleRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                request.SubscriptionId = await SubscriptionEndpointHelpers.ResolveSubscriptionIdAsync(subscriptionService, user, request.SubscriptionId);
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<LifecycleResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(LifecycleRequest request, ISubscriptionService subscriptionService)
    {
        var response = new LifecycleResponse(request.CorrelationId());
        var subscriptionId = request.SubscriptionId!.Value;

        Subscription subscription = request.Action switch
        {
            LifecycleAction.Pause => await subscriptionService.PauseAsync(subscriptionId),
            LifecycleAction.Resume => await subscriptionService.ResumeAsync(subscriptionId),
            LifecycleAction.Cancel => await subscriptionService.CancelAsync(subscriptionId, request.EndOfPeriod, request.Reason),
            LifecycleAction.Reactivate => await subscriptionService.ReactivateAsync(subscriptionId),
            _ => throw new ArgumentOutOfRangeException(nameof(request.Action), request.Action, "Unknown lifecycle action."),
        };

        response.Subscription = SubscriptionDto.FromEntity(subscription);
        return Results.Ok(response);
    }
}
