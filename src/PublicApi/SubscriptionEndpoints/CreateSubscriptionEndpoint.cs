using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan.
/// <para>
/// Idempotent by design: the shopper's billing customer is looked up by a reference derived from
/// their login name and only created when absent, and an existing live subscription to the same
/// plan is returned instead of a second one being created. A double-click therefore produces one
/// customer and one subscription, and the second call answers 200 rather than 201.
/// </para>
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ClaimsPrincipal, UserManager<ApplicationUser>>
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly IMapper _mapper;

    public CreateSubscriptionEndpoint(ISubscriptionService subscriptionService, IMapper mapper)
    {
        _subscriptionService = subscriptionService;
        _mapper = mapper;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest? request, ClaimsPrincipal user, UserManager<ApplicationUser> userManager, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(request ?? new CreateSubscriptionRequest(), user, userManager, cancellationToken);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request, ClaimsPrincipal user, UserManager<ApplicationUser> userManager) =>
        HandleAsync(request, user, userManager, CancellationToken.None);

    public async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        ClaimsPrincipal user,
        UserManager<ApplicationUser> userManager,
        CancellationToken cancellationToken)
    {
        var subscriber = await SubscriberIdentityResolver.ResolveAsync(user, userManager);
        if (subscriber is null)
        {
            return Results.Unauthorized();
        }

        var result = await _subscriptionService.SubscribeAsync(
            new SubscribeRequest(subscriber, request.PlanHandle),
            cancellationToken);

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = _mapper.Map<SubscriptionDto>(result.Subscription),
            Plan = _mapper.Map<SubscriptionPlanDto>(result.Plan),
            AlreadySubscribed = result.AlreadySubscribed
        };

        // An idempotent replay created nothing, so it is not a 201.
        return result.AlreadySubscribed
            ? Results.Ok(response)
            : Results.Created("api/my-subscriptions", response);
    }
}
