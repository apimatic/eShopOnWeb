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
/// Enroll the caller in a plan (UC1). Administrators may enroll another user by supplying
/// <see cref="SubscribeRequest.UserReference"/>.
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                SubscribeRequest request,
                ClaimsPrincipal user,
                ISubscriptionService subscriptionService,
                CancellationToken cancellationToken) =>
            {
                var reference = SubscriptionCaller.ResolveUserReference(user, request.UserReference);
                if (reference is null)
                {
                    return SubscriptionCaller.Forbidden();
                }

                request.UserReference = reference;
                return await HandleAsync(request, subscriptionService, cancellationToken);
            })
            .Produces<SubscribeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(SubscribeRequest request, ISubscriptionService subscriptionService)
        => HandleAsync(request, subscriptionService, CancellationToken.None);

    public async Task<IResult> HandleAsync(
        SubscribeRequest request,
        ISubscriptionService subscriptionService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest("planHandle is required.");
        }

        var subscriber = new SubscriberIdentity(request.UserReference!, request.UserReference);
        var subscription = await subscriptionService.SubscribeAsync(subscriber, request.PlanHandle, cancellationToken);

        var response = new SubscribeResponse(request.CorrelationId())
        {
            Subscription = SubscriptionDto.From(subscription)
        };

        return Results.Ok(response);
    }
}

public class SubscribeRequest : BaseRequest
{
    /// <summary>The durable handle of the plan to enroll in.</summary>
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>
    /// The user to enroll. Ignored for non-administrators, who may only enroll themselves.
    /// </summary>
    public string? UserReference { get; set; }
}

public class SubscribeResponse : BaseResponse
{
    public SubscribeResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscribeResponse()
    {
    }

    public SubscriptionDto? Subscription { get; set; }
}
