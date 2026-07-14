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

/// <summary>One management surface for the four UC4 lifecycle actions: pause, resume, cancel, reactivate.</summary>
public class LifecycleEndpoint : IEndpoint<IResult, LifecycleRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/lifecycle",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, LifecycleRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                request.SubscriptionId = subscriptionId;
                request.BuyerId = user.Identity!.Name!;
                request.IsAdmin = user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<LifecycleResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(LifecycleRequest request, ISubscriptionService subscriptionService)
    {
        var response = new LifecycleResponse(request.CorrelationId());

        var subscription = request.Action.Trim().ToLowerInvariant() switch
        {
            "pause" => await subscriptionService.PauseSubscriptionAsync(request.SubscriptionId, request.BuyerId, request.IsAdmin),
            "resume" => await subscriptionService.ResumeSubscriptionAsync(request.SubscriptionId, request.BuyerId, request.IsAdmin),
            "cancel" => await subscriptionService.CancelSubscriptionAsync(
                request.SubscriptionId, request.BuyerId, request.IsAdmin, ParseCancellationTiming(request.CancellationTiming), request.Reason),
            "reactivate" => await subscriptionService.ReactivateSubscriptionAsync(request.SubscriptionId, request.BuyerId, request.IsAdmin),
            _ => throw new ArgumentException($"Unrecognized lifecycle action '{request.Action}'; expected Pause, Resume, Cancel, or Reactivate.", nameof(request))
        };

        response.Subscription = SubscriptionDto.FromDomain(subscription);

        return Results.Ok(response);
    }

    private static CancellationTiming ParseCancellationTiming(string? timing)
    {
        if (string.IsNullOrWhiteSpace(timing))
        {
            return CancellationTiming.Immediate;
        }

        if (Enum.TryParse<CancellationTiming>(timing, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        throw new ArgumentException($"Unrecognized cancellation timing '{timing}'; expected 'Immediate' or 'EndOfPeriod'.", nameof(timing));
    }
}
