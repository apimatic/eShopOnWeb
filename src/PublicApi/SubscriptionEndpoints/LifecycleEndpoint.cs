using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// One management surface for the four lifecycle actions — pause, resume, cancel and reactivate
/// (UC4). Illegal transitions are rejected before any provider call.
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
            LifecycleAction.Pause => await subscriptionService.PauseAsync(request.SubscriptionId),
            LifecycleAction.Resume => await subscriptionService.ResumeAsync(request.SubscriptionId),
            LifecycleAction.Cancel => await subscriptionService.CancelAsync(request.SubscriptionId, request.Timing, request.Reason),
            LifecycleAction.Reactivate => await subscriptionService.ReactivateAsync(request.SubscriptionId),
            _ => throw new InvalidSubscriptionOperationException(
                $"'{request.Action}' is not a supported lifecycle action. Use Pause, Resume, Cancel or Reactivate.")
        };

        response.Action = request.Action.ToString();
        response.Subscription = subscription.ToDto();
        response.EffectiveAt = request.Action == LifecycleAction.Cancel && request.Timing == CancellationTiming.EndOfPeriod
            ? subscription.DelayedCancelAt
            : null;

        return Results.Ok(response);
    }
}
