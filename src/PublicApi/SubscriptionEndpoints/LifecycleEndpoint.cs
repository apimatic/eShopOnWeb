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
/// Apply a lifecycle transition — pause, resume, cancel, cancel at end of period, or reactivate
/// (UC4). Customers may act on their own subscription; administrators on any.
/// </summary>
public class LifecycleEndpoint : IEndpoint<IResult, LifecycleRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/lifecycle",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, LifecycleRequest request, ClaimsPrincipal user,
             ISubscriptionService subscriptionService, CancellationToken cancellationToken) =>
            {
                request.SubscriptionId = subscriptionId;
                request.User = user;
                request.CancellationToken = cancellationToken;
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<LifecycleResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(LifecycleRequest request, ISubscriptionService subscriptionService) =>
        SubscriptionEndpointSupport.ExecuteAsync(async () =>
        {
            var denied = await SubscriptionEndpointSupport.EnsureCallerMayActOnAsync(
                request.User, request.SubscriptionId, subscriptionService, request.CancellationToken);

            if (denied is not null)
            {
                return denied;
            }

            var action = SubscriptionEndpointSupport.ParseAction(request.Action);
            var response = new LifecycleResponse(request.CorrelationId());

            var subscription = await subscriptionService.ApplyLifecycleActionAsync(
                request.SubscriptionId, action, request.Reason, request.CancellationToken);

            response.Subscription = SubscriptionEndpointSupport.ToDto(subscription);

            return Results.Ok(response);
        });
}
