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
/// Applies a lifecycle transition — pause, resume, cancel, or reactivate — to a subscription (UC4).
/// </summary>
public class LifecycleEndpoint : IEndpoint<IResult, LifecycleRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/lifecycle",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int subscriptionId,
                LifecycleRequest request,
                ClaimsPrincipal user,
                ISubscriptionService subscriptionService,
                CancellationToken cancellationToken) =>
            {
                request.Bind(subscriptionId, user, cancellationToken);
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<LifecycleResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(LifecycleRequest request, ISubscriptionService subscriptionService)
    {
        if (!request.TryParseAction(out var action))
        {
            return Results.BadRequest("Action must be one of 'Pause', 'Resume', 'Cancel', or 'Reactivate'.");
        }

        if (!request.TryParseCancellationTiming(out var timing))
        {
            return Results.BadRequest("CancellationTiming must be either 'Immediate' or 'EndOfPeriod'.");
        }

        var actor = SubscriptionActorResolver.Resolve(request.User);
        if (actor is null)
        {
            return Results.Unauthorized();
        }

        var response = new LifecycleResponse(request.CorrelationId());

        var subscription = await subscriptionService.ApplyLifecycleActionAsync(
            actor,
            request.SubscriptionId,
            action,
            timing,
            request.Reason,
            request.CancellationToken);

        response.Subscription = SubscriptionDto.FromSubscription(subscription);

        return Results.Ok(response);
    }
}

public class LifecycleRequest : BaseRequest
{
    /// <summary><c>Pause</c>, <c>Resume</c>, <c>Cancel</c>, or <c>Reactivate</c>.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>For a cancellation: <c>Immediate</c> or <c>EndOfPeriod</c>. Ignored otherwise.</summary>
    public string CancellationTiming { get; set; } = "Immediate";

    /// <summary>An optional reason recorded with the transition.</summary>
    public string? Reason { get; set; }

    internal int SubscriptionId { get; private set; }

    internal ClaimsPrincipal? User { get; private set; }

    internal CancellationToken CancellationToken { get; private set; }

    internal void Bind(int subscriptionId, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        SubscriptionId = subscriptionId;
        User = user;
        CancellationToken = cancellationToken;
    }

    internal bool TryParseAction(out SubscriptionLifecycleAction action) =>
        Enum.TryParse(Action, ignoreCase: true, out action) && Enum.IsDefined(action);

    internal bool TryParseCancellationTiming(out CancellationTiming timing) =>
        Enum.TryParse(CancellationTiming, ignoreCase: true, out timing) && Enum.IsDefined(timing);
}

public class LifecycleResponse : BaseResponse
{
    public LifecycleResponse(Guid correlationId) : base(correlationId)
    {
    }

    public LifecycleResponse()
    {
    }

    public SubscriptionDto? Subscription { get; set; }
}
