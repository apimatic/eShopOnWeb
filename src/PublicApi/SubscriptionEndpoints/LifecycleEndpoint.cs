using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>UC4 — one management surface for pause / resume / cancel / reactivate.</summary>
public class LifecycleEndpoint : IEndpoint<IResult, LifecycleRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/lifecycle",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, LifecycleRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                request.SubscriptionId = subscriptionId;
                request.UserName = user.Identity?.Name ?? string.Empty;
                request.IsAdministrator = user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<LifecycleResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(LifecycleRequest request, ISubscriptionService subscriptionService)
    {
        Guard.Against.NullOrEmpty(request.UserName, nameof(request.UserName));

        if (!await SubscriptionAccessControl.CanAccessAsync(subscriptionService, request.UserName, request.IsAdministrator, request.SubscriptionId))
        {
            return Results.Forbid();
        }

        Subscription subscription = request.Action switch
        {
            LifecycleAction.Pause => await subscriptionService.PauseAsync(request.SubscriptionId),
            LifecycleAction.Resume => await subscriptionService.ResumeAsync(request.SubscriptionId),
            LifecycleAction.Cancel => await subscriptionService.CancelAsync(request.SubscriptionId, request.EndOfPeriod, request.Reason),
            LifecycleAction.Reactivate => await subscriptionService.ReactivateAsync(request.SubscriptionId),
            _ => throw new ArgumentOutOfRangeException(nameof(request), request.Action, "Unknown lifecycle action.")
        };

        var response = new LifecycleResponse(request.CorrelationId())
        {
            Subscription = SubscriptionDto.FromDomain(subscription)
        };

        return Results.Ok(response);
    }
}
