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
/// Applies a lifecycle transition — pause, resume, cancel (immediate or end-of-period) or reactivate
/// (plan.md UC4). A customer may act on their own subscription; an administrator on any.
/// </summary>
public class LifecycleEndpoint : IEndpoint<IResult, LifecycleRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/lifecycle",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, LifecycleRequest request, HttpContext http,
                ISubscriptionService subscriptionService, CancellationToken cancellationToken) =>
            {
                request.SetContext(subscriptionId, SubscriptionCaller.Restriction(http.User));
                return await HandleAsync(request, subscriptionService, cancellationToken);
            })
            .Produces<LifecycleResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(LifecycleRequest request, ISubscriptionService subscriptionService) =>
        HandleAsync(request, subscriptionService, CancellationToken.None);

    public async Task<IResult> HandleAsync(LifecycleRequest request, ISubscriptionService subscriptionService,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<SubscriptionLifecycleAction>(request.Action, ignoreCase: true, out var action) ||
            !Enum.IsDefined(action))
        {
            return Results.BadRequest(
                $"action must be one of: {string.Join(", ", Enum.GetNames<SubscriptionLifecycleAction>())}.");
        }

        var updated = await subscriptionService.ApplyLifecycleActionAsync(
            request.SubscriptionId, action, request.Reason, request.RestrictToUserReference, cancellationToken);

        return Results.Ok(new LifecycleResponse(request.CorrelationId())
        {
            Subscription = SubscriptionDto.From(updated)
        });
    }
}
