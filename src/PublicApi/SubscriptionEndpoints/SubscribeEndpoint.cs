using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Enrols the authenticated user in a plan (UC1). Repeating the call while a subscription is
/// already live returns that subscription rather than creating a second one.
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
                request.Bind(user, cancellationToken);
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<SubscribeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, ISubscriptionService subscriptionService)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest("A plan handle is required.");
        }

        var userName = SubscriptionActorResolver.ResolveUserName(request.User);
        if (userName is null)
        {
            return Results.Unauthorized();
        }

        var response = new SubscribeResponse(request.CorrelationId());

        var subscription = await subscriptionService.SubscribeAsync(
            ApplicationCore.Entities.SubscriptionAggregate.SubscriptionActor.Customer(userName),
            request.PlanHandle,
            request.CancellationToken);

        response.Subscription = SubscriptionDto.FromSubscription(subscription);

        return Results.Ok(response);
    }
}

public class SubscribeRequest : BaseRequest
{
    /// <summary>The stable handle of the plan to enrol in, for example <c>eshop-pro</c>.</summary>
    public string PlanHandle { get; set; } = string.Empty;

    internal ClaimsPrincipal? User { get; private set; }

    internal CancellationToken CancellationToken { get; private set; }

    internal void Bind(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        User = user;
        CancellationToken = cancellationToken;
    }
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
