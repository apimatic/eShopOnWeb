using System;
using System.Threading;
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
/// Apply a lifecycle action to a subscription: pause, resume, cancel or reactivate
/// </summary>
public class LifecycleEndpoint : IEndpoint<IResult, LifecycleRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/lifecycle",
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

        var subscription = request.Action switch
        {
            LifecycleAction.Pause => await subscriptionService.PauseAsync(request.SubscriptionId, CancellationToken.None),
            LifecycleAction.Resume => await subscriptionService.ResumeAsync(request.SubscriptionId, CancellationToken.None),
            LifecycleAction.Cancel => await subscriptionService.CancelAsync(request.SubscriptionId, request.Timing,
                request.Reason, CancellationToken.None),
            LifecycleAction.Reactivate => await subscriptionService.ReactivateAsync(request.SubscriptionId, CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(request), request.Action, "Unsupported lifecycle action.")
        };

        response.Subscription = subscription.ToDto();

        return Results.Ok(response);
    }
}
