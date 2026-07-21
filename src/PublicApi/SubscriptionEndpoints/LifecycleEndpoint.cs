using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using BlazorShared.Authorization;
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
/// One management surface, four lifecycle actions (UC4). A caller may only manage their own
/// subscription unless they hold the Administrator role.
/// </summary>
public class LifecycleEndpoint : IEndpoint<IResult, LifecycleRequest, ClaimsPrincipal>
{
    private readonly ISubscriptionService _subscriptionService;

    public LifecycleEndpoint(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/lifecycle",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (LifecycleRequest request, ClaimsPrincipal user) =>
            {
                return await HandleAsync(request, user);
            })
            .Produces<LifecycleResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(LifecycleRequest request, ClaimsPrincipal user)
    {
        Guard.Against.Null(user.Identity?.Name, nameof(user.Identity.Name));
        var userReference = user.Identity!.Name!;

        if (!user.IsInRole(Constants.Roles.ADMINISTRATORS))
        {
            var mine = await _subscriptionService.GetMySubscriptionsAsync(userReference);
            if (!mine.Any(s => s.Id == request.SubscriptionId))
            {
                return Results.Forbid();
            }
        }

        Subscription subscription = request.Action.ToLowerInvariant() switch
        {
            "pause" => await _subscriptionService.PauseAsync(userReference, request.SubscriptionId),
            "resume" => await _subscriptionService.ResumeAsync(userReference, request.SubscriptionId),
            "cancel" => await _subscriptionService.CancelAsync(userReference, request.SubscriptionId, request.EndOfPeriod),
            "reactivate" => await _subscriptionService.ReactivateAsync(userReference, request.SubscriptionId),
            _ => throw new BillingProviderException($"Unknown lifecycle action '{request.Action}'. Expected pause, resume, cancel, or reactivate.", BillingErrorKind.Validation)
        };

        var response = new LifecycleResponse(request.CorrelationId())
        {
            Subscription = subscription.ToDto()
        };
        return Results.Ok(response);
    }
}
