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
/// Pause, resume, cancel or reactivate a subscription (UC4).
/// A customer may act on their own subscription; an administrator on any.
/// </summary>
public class LifecycleEndpoint : IEndpoint<IResult, LifecycleRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/lifecycle",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, LifecycleRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService, CancellationToken cancellationToken) =>
            {
                request.SubscriptionId = subscriptionId;

                return await HandleAsync(request, user.OwnershipScope(), subscriptionService, cancellationToken);
            })
            .Produces<LifecycleResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(LifecycleRequest request, ISubscriptionService subscriptionService) =>
        HandleAsync(request, null, subscriptionService, CancellationToken.None);

    public async Task<IResult> HandleAsync(LifecycleRequest request, string? ownershipScope, ISubscriptionService subscriptionService, CancellationToken cancellationToken)
    {
        var result = await subscriptionService.ApplyLifecycleActionAsync(
            ownershipScope, request.SubscriptionId, request.Action, request.EndOfPeriod, request.Reason, cancellationToken);

        var response = new LifecycleResponse(request.CorrelationId())
        {
            Action = result.Action,
            PreviousState = result.PreviousState,
            NewState = result.NewState,
            EffectiveAt = result.EffectiveAt,
            Subscription = SubscriptionDto.From(result.Subscription)
        };

        return Results.Ok(response);
    }
}
