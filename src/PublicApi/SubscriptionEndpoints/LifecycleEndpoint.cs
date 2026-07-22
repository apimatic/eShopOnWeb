using System;
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
/// One surface for the four lifecycle actions — pause, resume, cancel, reactivate (UC4).
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
            (int subscriptionId, LifecycleRequest request, ISubscriptionService subscriptionService) =>
            {
                request.SubscriptionId = subscriptionId;
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<LifecycleResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(LifecycleRequest request, ISubscriptionService subscriptionService)
    {
        var response = new LifecycleResponse(request.CorrelationId());
        var action = request.Action?.Trim().ToLowerInvariant();

        var subscription = action switch
        {
            SubscriptionActions.Pause => await subscriptionService.PauseAsync(request.SubscriptionId),
            SubscriptionActions.Resume => await subscriptionService.ResumeAsync(request.SubscriptionId),
            SubscriptionActions.Cancel => await subscriptionService.CancelAsync(
                request.SubscriptionId, request.ResolveCancellationTiming(), request.Reason),
            SubscriptionActions.Reactivate => await subscriptionService.ReactivateAsync(request.SubscriptionId),
            _ => throw new ArgumentException(
                $"'{request.Action}' is not a lifecycle action. Use one of: " +
                $"{SubscriptionActions.Pause}, {SubscriptionActions.Resume}, " +
                $"{SubscriptionActions.Cancel}, {SubscriptionActions.Reactivate}.", nameof(request))
        };

        response.Subscription = _mapper.Map<SubscriptionDto>(subscription);

        return Results.Ok(response);
    }
}
