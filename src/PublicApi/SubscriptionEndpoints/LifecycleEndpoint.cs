using System.Security.Claims;
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
/// Applies a lifecycle transition — pause, resume, cancel or reactivate — to the caller's own
/// subscription (UC4).
/// </summary>
public class LifecycleEndpoint : IEndpoint<IResult, LifecycleRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/lifecycle",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (long subscriptionId, LifecycleRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                request.SubscriptionId = subscriptionId;
                request.UserName = user.Identity?.Name;
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<LifecycleResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(LifecycleRequest request, ISubscriptionService subscriptionService)
    {
        if (string.IsNullOrWhiteSpace(request.UserName))
        {
            return Results.Unauthorized();
        }

        var response = new LifecycleResponse(request.CorrelationId());

        var subscription = await subscriptionService.ApplyLifecycleActionAsync(
            request.UserName, request.SubscriptionId, request.Action, request.CancellationTiming, request.Reason);

        response.Subscription = SubscriptionDto.FromSubscription(subscription);

        return Results.Ok(response);
    }
}
