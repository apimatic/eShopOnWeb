using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Applies a lifecycle transition to a subscription: Pause, Resume, CancelImmediately,
/// CancelAtPeriodEnd or Reactivate. Administrators may act on any subscription; other callers only on
/// their own.
/// </summary>
public class LifecycleEndpoint : IEndpoint<IResult, LifecycleRequest, ISubscriptionService>
{
    private readonly IMapper _mapper;

    public LifecycleEndpoint(IMapper mapper)
    {
        _mapper = mapper;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/lifecycle",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, LifecycleRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService, CancellationToken cancellationToken) =>
            {
                request.SubscriptionId = subscriptionId;
                request.OwnerReference = SubscriptionCaller.ResolveOwnerReference(user);
                request.IsAuthenticated = user.Identity?.Name is not null;

                return await HandleAsync(request, subscriptionService, cancellationToken);
            })
            .Produces<LifecycleResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(LifecycleRequest request, ISubscriptionService subscriptionService)
    {
        return HandleAsync(request, subscriptionService, CancellationToken.None);
    }

    public async Task<IResult> HandleAsync(LifecycleRequest request, ISubscriptionService subscriptionService, CancellationToken cancellationToken)
    {
        if (!request.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        return await SubscriptionErrorResults.ExecuteAsync(async () =>
        {
            var subscription = await subscriptionService.ApplyLifecycleActionAsync(
                request.SubscriptionId,
                request.Action,
                request.Reason,
                request.OwnerReference,
                cancellationToken);

            var response = new LifecycleResponse(request.CorrelationId())
            {
                Subscription = _mapper.Map<SubscriptionDto>(subscription)
            };

            return Results.Ok(response);
        });
    }
}
