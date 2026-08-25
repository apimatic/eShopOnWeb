using System.Security.Claims;
using System.Threading.Tasks;
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
/// Subscribes the authenticated user to a plan. Idempotent: if the user already has an
/// active subscription to the plan, the existing subscription is returned.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ClaimsPrincipal>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ISubscriptionBillingService _billingService;

    public CreateSubscriptionEndpoint(UserManager<ApplicationUser> userManager, ISubscriptionBillingService billingService)
    {
        _userManager = userManager;
        _billingService = billingService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user) =>
            {
                return await HandleAsync(request, user);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ClaimsPrincipal user)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest("PlanHandle is required.");
        }

        var subscriber = await SubscriberInfoFactory.CreateAsync(user, _userManager);
        if (subscriber is null)
        {
            return Results.Unauthorized();
        }

        var subscription = await _billingService.SubscribeAsync(subscriber, request.PlanHandle.Trim());

        response.Subscription = Map(subscription);
        return Results.Created("api/my-subscriptions", response);
    }

    internal static SubscriptionDto Map(BillingSubscription subscription)
    {
        return new SubscriptionDto
        {
            SubscriptionId = subscription.SubscriptionId,
            PlanHandle = subscription.PlanHandle,
            PlanName = subscription.PlanName,
            State = subscription.State,
            Price = subscription.PriceInCents / 100m,
            Interval = subscription.Interval,
            IntervalUnit = subscription.IntervalUnit,
            Currency = subscription.Currency,
            ActivatedAt = subscription.ActivatedAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            NextBillingAt = subscription.NextBillingAt
        };
    }
}
