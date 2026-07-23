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
/// Apply a lifecycle transition — pause, resume, cancel now, cancel at end of period, or reactivate (UC4).
/// </summary>
/// <remarks>
/// A transition that is illegal from the subscription's current state is rejected without any provider
/// call, and the response says which transitions are available instead.
/// </remarks>
public class LifecycleEndpoint : IEndpoint<IResult, LifecycleRequest, ClaimsPrincipal, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/lifecycle",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, LifecycleRequest request, ClaimsPrincipal user,
                ISubscriptionService subscriptionService) =>
            {
                request.SubscriptionId = subscriptionId;
                return await HandleAsync(request, user, subscriptionService);
            })
            .Produces<LifecycleResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        LifecycleRequest request,
        ClaimsPrincipal user,
        ISubscriptionService subscriptionService)
    {
        var response = new LifecycleResponse(request.CorrelationId());

        var subscription = await subscriptionService.ExecuteLifecycleActionAsync(
            user.ToActor(),
            request.SubscriptionId,
            SubscriptionLifecycleRequest.For(request.Action, request.Reason));

        response.Subscription = subscription.ToDto();

        return Results.Ok(response);
    }
}
