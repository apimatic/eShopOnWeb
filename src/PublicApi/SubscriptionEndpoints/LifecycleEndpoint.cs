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
/// UC4 — pause / resume / cancel (immediate or end-of-period) / reactivate. Customers act on their
/// own subscription; admins (Roles=Administrators) may target any subscription.
/// </summary>
public class LifecycleEndpoint : IEndpoint<IResult, LifecycleRequest, SubscriptionEndpointContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/lifecycle",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, LifecycleRequest request, ISubscriptionService subscriptionService, ClaimsPrincipal user) =>
            {
                request.SubscriptionId = subscriptionId;
                return await HandleAsync(request, new SubscriptionEndpointContext(subscriptionService, user));
            })
            .Produces<LifecycleResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(LifecycleRequest request, SubscriptionEndpointContext context)
    {
        if (!Enum.TryParse<SubscriptionLifecycleAction>(request.Action, ignoreCase: true, out var action))
        {
            return Results.BadRequest(
                $"Invalid action '{request.Action}'. Expected one of: Pause, Resume, CancelImmediate, CancelAtEndOfPeriod, Reactivate.");
        }

        var response = new LifecycleResponse(request.CorrelationId());
        var ownerReference = SubscriptionEndpointHelpers.ResolveOwnerReference(context.User);

        var subscription = await context.SubscriptionService.ApplyLifecycleActionAsync(
            ownerReference, request.SubscriptionId, action, request.Reason);

        response.Subscription = SubscriptionDtoMapper.ToDto(subscription);
        return Results.Ok(response);
    }
}

public class LifecycleRequest : BaseRequest
{
    public int SubscriptionId { get; set; }
    public string Action { get; set; } = string.Empty;
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
