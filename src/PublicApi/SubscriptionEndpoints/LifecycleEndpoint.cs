using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.Subscriptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// One management surface for the four subscription lifecycle actions (UC4):
/// pause, resume, cancel (immediate or end-of-period), reactivate.
/// </summary>
public class LifecycleEndpoint : IEndpoint<IResult, LifecycleRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/lifecycle",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, LifecycleRequest request, ISubscriptionService subscriptionService,
                HttpContext httpContext) =>
            {
                request.SubscriptionId = subscriptionId;
                request.OwnerReference = SubscriptionEndpointHelpers.ResolveOwnerReference(httpContext.User);
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<LifecycleResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(LifecycleRequest request, ISubscriptionService subscriptionService)
    {
        var response = new LifecycleResponse(request.CorrelationId());

        CustomerSubscription subscription = request.Action.ToLowerInvariant() switch
        {
            "pause" => await subscriptionService.PauseAsync(request.OwnerReference, request.SubscriptionId),
            "resume" => await subscriptionService.ResumeAsync(request.OwnerReference, request.SubscriptionId),
            "cancel" => await subscriptionService.CancelAsync(request.OwnerReference, request.SubscriptionId,
                request.Reason, request.EndOfPeriod),
            "reactivate" => await subscriptionService.ReactivateAsync(request.OwnerReference, request.SubscriptionId),
            _ => throw new ArgumentException(
                $"Unknown lifecycle action '{request.Action}'. Expected Pause, Resume, Cancel, or Reactivate.")
        };

        response.Subscription = SubscriptionEndpointHelpers.ToDto(subscription);

        return Results.Ok(response);
    }
}
