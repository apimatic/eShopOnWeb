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
/// Subscribe the authenticated caller to a plan
/// </summary>
/// <remarks>
/// Idempotent per (caller, plan): a repeated or double-clicked request returns the subscription that already
/// exists instead of enrolling the shopper twice.
/// </remarks>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, ISubscriptionBillingService>
{
    private readonly IMapper _mapper;

    public SubscribeEndpoint(IMapper mapper)
    {
        _mapper = mapper;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, ClaimsPrincipal user, ISubscriptionBillingService billingService, CancellationToken cancellationToken) =>
            {
                request.Caller = user;
                request.CancellationToken = cancellationToken;
                return await HandleAsync(request, billingService);
            })
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .Produces<SubscribeResponse>(StatusCodes.Status200OK)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, ISubscriptionBillingService billingService)
    {
        if (!SubscriptionCaller.TryResolve(request.Caller, out var identity))
        {
            return Results.Unauthorized();
        }

        var response = new SubscribeResponse(request.CorrelationId());

        var result = await billingService.SubscribeAsync(identity, request.PlanHandle, request.CancellationToken);

        response.Subscription = _mapper.Map<SubscriptionDto>(result.Subscription);
        response.AlreadySubscribed = result.AlreadySubscribed;

        // A repeat request created nothing, so it is not a 201. Saying so lets a client tell the two apart.
        return result.AlreadySubscribed
            ? Results.Ok(response)
            : Results.Created("api/my-subscriptions", response);
    }
}
