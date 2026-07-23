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
/// Apply a lifecycle transition — pause, resume, cancel or reactivate (UC4). A customer manages
/// their own subscription; an administrator may manage any user's.
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
        app.MapPost("api/subscriptions/lifecycle",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (LifecycleRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                request.UserName = SubscriptionActor.TryResolve(user, request.OnBehalfOfUserName, out var userName) ? userName : null;
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<LifecycleResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(LifecycleRequest request, ISubscriptionService subscriptionService)
    {
        if (string.IsNullOrWhiteSpace(request.UserName))
        {
            return Results.Forbid();
        }

        var response = new LifecycleResponse(request.CorrelationId());
        var userName = request.UserName!;

        Subscription subscription = request.Action switch
        {
            SubscriptionLifecycleAction.Pause => await subscriptionService.PauseAsync(userName),
            SubscriptionLifecycleAction.Resume => await subscriptionService.ResumeAsync(userName),
            SubscriptionLifecycleAction.Cancel => await subscriptionService.CancelAsync(userName, request.Timing, request.Reason),
            SubscriptionLifecycleAction.Reactivate => await subscriptionService.ReactivateAsync(userName),
            _ => throw new System.ArgumentOutOfRangeException(nameof(request), $"Unsupported lifecycle action '{request.Action}'.")
        };

        response.Subscription = _mapper.Map<SubscriptionDto>(subscription);

        return Results.Ok(response);
    }
}
