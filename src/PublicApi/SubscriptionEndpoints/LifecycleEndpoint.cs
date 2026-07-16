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
/// UC4: one management surface for the four lifecycle actions — pause, resume, cancel (immediate or
/// end-of-period), reactivate.
/// </summary>
public class LifecycleEndpoint : IEndpoint<IResult, LifecycleRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/lifecycle",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, LifecycleRequest request, ClaimsPrincipal principal, ISubscriptionService subscriptionService) =>
            {
                request.SubscriptionId = subscriptionId;
                request.CustomerReference = principal.Identity!.Name!;
                request.IsAdmin = principal.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);

                return await HandleAsync(request, subscriptionService);
            })
            .Produces<LifecycleResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(LifecycleRequest request, ISubscriptionService subscriptionService)
    {
        var response = new LifecycleResponse(request.CorrelationId());

        BillingSubscription subscription = request.Action.ToLowerInvariant() switch
        {
            "pause" => await subscriptionService.PauseAsync(request.CustomerReference, request.SubscriptionId, request.IsAdmin),
            "resume" => await subscriptionService.ResumeAsync(request.CustomerReference, request.SubscriptionId, request.IsAdmin),
            "cancel" => await subscriptionService.CancelAsync(request.CustomerReference, request.SubscriptionId, request.EndOfPeriod, request.Reason, request.IsAdmin),
            "reactivate" => await subscriptionService.ReactivateAsync(request.CustomerReference, request.SubscriptionId, request.IsAdmin),
            _ => throw new ArgumentException($"Unknown lifecycle action '{request.Action}'. Expected pause, resume, cancel, or reactivate.")
        };

        response.Subscription = subscription;

        return Results.Ok(response);
    }
}
