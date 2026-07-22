using System;
using System.Security.Claims;
using System.Threading.Tasks;
using AutoMapper;
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
/// Apply a lifecycle transition — pause, resume, cancel or reactivate (UC4)
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
            (int subscriptionId, LifecycleRequest request, ClaimsPrincipal user,
                ISubscriptionService subscriptionService) =>
            {
                request.SubscriptionId = subscriptionId;
                request.OwnerBuyerId = SubscriptionCaller.ResolveOwnerBuyerId(user);

                return await HandleAsync(request, subscriptionService);
            })
            .Produces<LifecycleResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(LifecycleRequest request, ISubscriptionService subscriptionService)
    {
        var response = new LifecycleResponse(request.CorrelationId());

        var subscription = request.Action switch
        {
            LifecycleAction.Pause => await subscriptionService.PauseAsync(request.SubscriptionId,
                request.OwnerBuyerId, request.AutomaticallyResumeAt),
            LifecycleAction.Resume => await subscriptionService.ResumeAsync(request.SubscriptionId,
                request.OwnerBuyerId),
            LifecycleAction.Cancel => await subscriptionService.CancelAsync(request.SubscriptionId,
                request.OwnerBuyerId, request.Timing, request.Reason),
            LifecycleAction.Reactivate => await subscriptionService.ReactivateAsync(request.SubscriptionId,
                request.OwnerBuyerId),
            _ => throw new ArgumentException($"Unknown lifecycle action '{request.Action}'.", nameof(request))
        };

        response.Subscription = _mapper.Map<SubscriptionDto>(subscription);

        return Results.Ok(response);
    }
}
