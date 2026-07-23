using System.Security.Claims;
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
/// Apply a lifecycle transition — pause, resume, cancel (immediate or end-of-period), reactivate —
/// to a subscription (UC4).
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
            (int subscriptionId, LifecycleRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                request.SubscriptionId = subscriptionId;
                request.OwnerReference = SubscriptionCaller.ResolveOwnerScope(user);
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<LifecycleResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(LifecycleRequest request, ISubscriptionService subscriptionService)
    {
        var response = new LifecycleResponse(request.CorrelationId());

        var subscription = await subscriptionService.ApplyLifecycleActionAsync(
            request.SubscriptionId,
            request.OwnerReference,
            request.ResolveAction(),
            request.ResolveCancellationTiming(),
            request.Reason);

        response.Subscription = _mapper.Map<SubscriptionDto>(subscription);

        return Results.Ok(response);
    }
}
