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
/// Pause, resume, cancel or reactivate a subscription (UC4). One surface, four transitions.
/// </summary>
public class LifecycleEndpoint : IEndpoint<IResult, LifecycleRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/lifecycle",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, HttpRequest httpRequest, ClaimsPrincipal user,
                ISubscriptionService subscriptionService, CancellationToken cancellationToken) =>
            {
                var request = LifecycleRequest.From(await SubscriptionRequestBody.ReadAsync(httpRequest, cancellationToken));
                return await HandleAsync(subscriptionId, request, user, subscriptionService, cancellationToken);
            })
            .Accepts<LifecycleRequest>("application/json")
            .Produces<LifecycleResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(LifecycleRequest request, ISubscriptionService subscriptionService)
        => HandleAsync(0, request, new ClaimsPrincipal(), subscriptionService, CancellationToken.None);

    public async Task<IResult> HandleAsync(int subscriptionId, LifecycleRequest request, ClaimsPrincipal user,
        ISubscriptionService subscriptionService, CancellationToken cancellationToken)
    {
        var action = request.ResolveAction();
        var timing = request.ResolveCancellationTiming();

        var result = await subscriptionService.ApplyLifecycleActionAsync(user.ToSubscriptionActor(), subscriptionId,
            action, timing, request.Reason, cancellationToken);

        return Results.Ok(new LifecycleResponse(request.CorrelationId())
        {
            Subscription = result.Subscription.ToDto(),
            Action = result.Action.ToString(),
            PreviousState = result.PreviousState.ToString(),
            NewState = result.NewState.ToString(),
            EffectiveAt = result.EffectiveAt,
            Message = result.Message
        });
    }
}
