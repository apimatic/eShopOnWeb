using System;
using System.Security.Claims;
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
/// One surface for the four lifecycle actions — pause, resume, cancel, reactivate (UC4).
/// </summary>
/// <remarks>
/// An action that is not legal from the subscription's current state is rejected with 409 and no
/// call is made to the billing provider.
/// </remarks>
public class LifecycleEndpoint : IEndpoint<IResult, LifecycleRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/lifecycle",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, LifecycleRequest request, ClaimsPrincipal user,
             ISubscriptionService subscriptionService, CancellationToken cancellationToken) =>
            {
                request.Bind(subscriptionId, user.ResolveActingScope(), cancellationToken);
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<LifecycleResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(LifecycleRequest request, ISubscriptionService subscriptionService)
    {
        var response = new LifecycleResponse(request.CorrelationId());

        var subscription = request.ResolveAction() switch
        {
            LifecycleAction.Pause => await subscriptionService.PauseAsync(
                request.SubscriptionId, request.ActingUserReference, request.CancellationToken),

            LifecycleAction.Resume => await subscriptionService.ResumeAsync(
                request.SubscriptionId, request.ActingUserReference, request.CancellationToken),

            LifecycleAction.Cancel => await subscriptionService.CancelAsync(
                request.SubscriptionId, request.ResolveCancellationTiming(), request.Reason,
                request.ActingUserReference, request.CancellationToken),

            LifecycleAction.Reactivate => await subscriptionService.ReactivateAsync(
                request.SubscriptionId, request.ActingUserReference, request.CancellationToken),

            _ => throw new ArgumentException($"'{request.Action}' is not a supported lifecycle action.")
        };

        response.Subscription = subscription.ToDto();

        return Results.Ok(response);
    }
}

/// <summary>The lifecycle transitions this endpoint accepts.</summary>
internal enum LifecycleAction
{
    Pause,
    Resume,
    Cancel,
    Reactivate
}
