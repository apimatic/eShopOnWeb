using System.Security.Claims;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribe the authenticated shopper to a plan.
/// </summary>
/// <remarks>
/// The request is idempotent: submitting it twice returns the same subscription and bills once.
/// A fresh enrollment answers 201 Created; an idempotent replay answers 200 OK with
/// <c>alreadySubscribed: true</c>.
/// </remarks>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ClaimsPrincipal, ISubscriptionBillingService>
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
            (CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionBillingService subscriptionBillingService) =>
            {
                return await HandleAsync(request, user, subscriptionBillingService);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .WithTags("SubscriptionEndpoints");
    }

    // No CancellationToken by design: enrolling a subscriber is a billing write, and abandoning it
    // because the client disconnected would leave the outcome unknown to both sides. The provider
    // call is bounded by its own timeout instead.
    public async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        ClaimsPrincipal user,
        ISubscriptionBillingService subscriptionBillingService)
    {
        var subscriber = user.ToSubscriber();
        if (subscriber is null)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest(new { message = "planHandle is required." });
        }

        var result = await subscriptionBillingService.SubscribeAsync(
            new SubscribeRequest(subscriber, request.PlanHandle.Trim(), request.IdempotencyKey?.Trim()));

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = _mapper.Map<SubscriptionDto>(result.Subscription),
            AlreadySubscribed = result.AlreadySubscribed
        };

        // There is no per-subscription route, so Location points at the collection the new
        // subscription now belongs to.
        return result.AlreadySubscribed
            ? Results.Ok(response)
            : Results.Created("/api/my-subscriptions", response);
    }
}
