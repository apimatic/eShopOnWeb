using System;
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
/// Applies a lifecycle transition — pause, resume, cancel or reactivate — to a subscription (UC4).
/// <para>
/// Administrators may act on any subscription; every other caller is restricted to their own, which
/// is enforced in the domain service rather than here.
/// </para>
/// </summary>
public class LifecycleEndpoint : IEndpoint<IResult, LifecycleRequest, ISubscriptionService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public LifecycleEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/lifecycle",
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
        var userReference = _httpContextAccessor.CurrentUserReference();
        if (userReference is null)
        {
            return Results.Unauthorized();
        }

        if (!Enum.TryParse<SubscriptionLifecycleAction>(request.Action, ignoreCase: true, out var action)
            || !Enum.IsDefined(action))
        {
            return Results.BadRequest("Action must be one of: Pause, Resume, Cancel, Reactivate.");
        }

        var timing = CancellationTiming.Immediate;
        if (!string.IsNullOrWhiteSpace(request.CancellationTiming)
            && (!Enum.TryParse(request.CancellationTiming, ignoreCase: true, out timing) || !Enum.IsDefined(timing)))
        {
            return Results.BadRequest("CancellationTiming must be 'Immediate' or 'EndOfPeriod'.");
        }

        var subscription = _httpContextAccessor.IsAdministrator()
            ? await subscriptionService.ApplyLifecycleActionForSubscriptionAsync(request.SubscriptionId, action, timing, request.Reason)
            : await subscriptionService.ApplyLifecycleActionAsync(userReference, request.SubscriptionId, action, timing, request.Reason);

        return Results.Ok(new LifecycleResponse(request.CorrelationId())
        {
            Subscription = subscription.ToDto()
        });
    }
}
