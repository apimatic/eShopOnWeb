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
/// UC4: pause / resume / cancel (immediate or end-of-period) / reactivate - one management surface,
/// four lifecycle actions, dispatched on <see cref="LifecycleRequest.Action"/>.
/// </summary>
public class LifecycleEndpoint : IEndpoint<IResult, LifecycleRequest, SubscriptionEndpointContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/lifecycle",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, LifecycleRequest request, ISubscriptionService subscriptionService, ClaimsPrincipal user) =>
            {
                request.SubscriptionId = subscriptionId;
                return await HandleAsync(request, SubscriptionEndpointContext.From(subscriptionService, user));
            })
            .Produces<LifecycleResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(LifecycleRequest request, SubscriptionEndpointContext context)
    {
        var response = new LifecycleResponse(request.CorrelationId());

        var updated = request.Action switch
        {
            LifecycleAction.Pause => await context.SubscriptionService.PauseAsync(context.UserId, request.SubscriptionId, context.IsAdmin),
            LifecycleAction.Resume => await context.SubscriptionService.ResumeAsync(context.UserId, request.SubscriptionId, context.IsAdmin),
            LifecycleAction.Cancel => await context.SubscriptionService.CancelAsync(context.UserId, request.SubscriptionId, request.CancelAtEndOfPeriod, request.Reason, context.IsAdmin),
            LifecycleAction.Reactivate => await context.SubscriptionService.ReactivateAsync(context.UserId, request.SubscriptionId, context.IsAdmin),
            _ => throw new ArgumentOutOfRangeException(nameof(request), request.Action, "Unknown lifecycle action.")
        };

        response.Subscription = SubscriptionMapping.ToDto(updated);

        return Results.Ok(response);
    }
}
