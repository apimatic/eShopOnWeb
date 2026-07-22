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
/// Applies a lifecycle transition — pause, resume, cancel (immediate or end-of-period), or
/// reactivate (UC4). Illegal transitions are rejected before any provider call.
/// </summary>
public class LifecycleEndpoint : IEndpoint<IResult, LifecycleRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscription-lifecycle",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (LifecycleRequest request, ISubscriptionService subscriptionService) =>
                await HandleAsync(request, subscriptionService))
            .Produces<LifecycleResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(LifecycleRequest request, ISubscriptionService subscriptionService)
    {
        var response = new LifecycleResponse(request.CorrelationId());

        var subscription = await subscriptionService.ApplyLifecycleActionAsync(request.SubscriptionId,
            request.Action, request.CancelAtEndOfPeriod, request.Reason);

        response.Subscription = subscription.ToDto();

        return Results.Ok(response);
    }
}
