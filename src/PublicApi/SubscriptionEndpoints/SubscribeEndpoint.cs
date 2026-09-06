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
/// Subscribe the signed-in shopper to a plan
/// </summary>
/// <remarks>
/// Idempotent by design: the billing customer and the subscription are both keyed on values derived
/// deterministically from the caller's identity, so repeating the call returns the existing subscription with
/// <c>alreadySubscribed</c> set rather than enrolling the shopper twice.
/// </remarks>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, ClaimsPrincipal, ISubscriptionBillingService>
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
            (SubscribeRequest request, ClaimsPrincipal user, ISubscriptionBillingService subscriptionBillingService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(request, user, subscriptionBillingService, cancellationToken);
            })
            .Produces<SubscribeResponse>()
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(SubscribeRequest request, ClaimsPrincipal user, ISubscriptionBillingService subscriptionBillingService) =>
        HandleAsync(request, user, subscriptionBillingService, CancellationToken.None);

    public async Task<IResult> HandleAsync(
        SubscribeRequest request,
        ClaimsPrincipal user,
        ISubscriptionBillingService subscriptionBillingService,
        CancellationToken cancellationToken)
    {
        var response = new SubscribeResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest("A planHandle is required. Call GET /api/subscription-plans to see the available handles.");
        }

        if (!SubscriptionCallerIdentity.TryResolve(user, out var identity, out var identityError))
        {
            return Results.BadRequest(identityError);
        }

        var result = await subscriptionBillingService.SubscribeAsync(identity, request.PlanHandle, cancellationToken);

        response.Subscription = _mapper.Map<SubscriptionDto>(result.Subscription);
        response.AlreadySubscribed = result.AlreadySubscribed;

        return result.AlreadySubscribed
            ? Results.Ok(response)
            : Results.Created("api/my-subscriptions", response);
    }
}
