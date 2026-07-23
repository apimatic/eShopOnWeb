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
/// Apply a lifecycle transition — pause, resume, cancel (immediate or end-of-period), or
/// reactivate (UC4).
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
                request.SubscriptionId = subscriptionId;

                if (!await SubscriptionAuthorization.CanActOnSubscriptionAsync(
                        user, subscriptionId, subscriptionService, cancellationToken))
                {
                    return SubscriptionCaller.Forbidden();
                }

                return await HandleAsync(request, subscriptionService, cancellationToken);
            })
            .Produces<LifecycleResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(LifecycleRequest request, ISubscriptionService subscriptionService)
        => HandleAsync(request, subscriptionService, CancellationToken.None);

    public async Task<IResult> HandleAsync(
        LifecycleRequest request,
        ISubscriptionService subscriptionService,
        CancellationToken cancellationToken)
    {
        var updated = await subscriptionService.ApplyLifecycleActionAsync(
            request.SubscriptionId,
            request.Action,
            request.CancellationTiming,
            request.Reason,
            cancellationToken);

        return Results.Ok(new LifecycleResponse(request.CorrelationId())
        {
            Subscription = SubscriptionDto.From(updated)
        });
    }
}

public class LifecycleRequest : BaseRequest
{
    /// <summary>Taken from the route; any value in the body is overwritten.</summary>
    public int SubscriptionId { get; set; }

    public SubscriptionLifecycleAction Action { get; set; }

    /// <summary>Only meaningful for <see cref="SubscriptionLifecycleAction.Cancel"/>.</summary>
    public CancellationTiming CancellationTiming { get; set; } = CancellationTiming.Immediate;

    public string? Reason { get; set; }
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
