using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using BlazorShared.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the caller to a plan.
/// </summary>
/// <remarks>
/// <para>
/// The operation is idempotent: a repeat of the same request - a double-clicked button, a retried
/// call - returns the subscription the first request created rather than creating a second one, and
/// answers 200 instead of 201 so the caller can tell the two apart.
/// </para>
/// <para>
/// Endpoint instances are created once, when routes are mapped, so per-request services are taken as
/// route handler parameters rather than through the constructor.
/// </para>
/// </remarks>
public class CreateSubscriptionEndpoint : IEndpoint
{
    private readonly IMapper _mapper;

    public CreateSubscriptionEndpoint(IMapper mapper)
    {
        _mapper = mapper;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ISubscriptionBillingService subscriptionBillingService,
                SubscriberAccountResolver accountResolver, ClaimsPrincipal user, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(request, subscriptionBillingService, accountResolver, user, cancellationToken);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionBillingService subscriptionBillingService,
        SubscriberAccountResolver accountResolver, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest(new ErrorDetails
            {
                StatusCode = StatusCodes.Status400BadRequest,
                Message = "A planHandle is required. Call GET /api/subscription-plans for the available handles."
            });
        }

        // The subscriber is taken from the bearer token, never from the request body.
        var account = await accountResolver.ResolveAsync(user, cancellationToken);
        if (account is null)
        {
            return Results.Unauthorized();
        }

        var result = await subscriptionBillingService.SubscribeAsync(account, request.PlanHandle.Trim(), cancellationToken);

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = _mapper.Map<SubscriptionDto>(result.Subscription),
            AlreadySubscribed = result.AlreadyExisted
        };

        return result.AlreadyExisted
            ? Results.Ok(response)
            : Results.Created("api/my-subscriptions", response);
    }
}
