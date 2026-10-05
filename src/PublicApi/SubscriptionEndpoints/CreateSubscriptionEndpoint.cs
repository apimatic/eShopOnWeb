using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a subscription plan. The Maxio customer for
/// the user is ensured first; enrolling is idempotent, so a double-click never creates
/// a second customer or subscription.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IMaxioSubscriptionService, UserManager<ApplicationUser>>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, IMaxioSubscriptionService subscriptionService,
             UserManager<ApplicationUser> userManager) =>
            {
                return await HandleAsync(request, subscriptionService, userManager);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioSubscriptionService subscriptionService,
        UserManager<ApplicationUser> userManager)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.Json(new
            {
                statusCode = 400,
                message = "A planHandle is required. Browse GET api/subscription-plans for available plans."
            }, statusCode: 400);
        }

        var user = await ResolveUserAsync(userManager, _httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal());
        if (user == null)
        {
            return Results.Unauthorized();
        }

        var subscription = await subscriptionService.SubscribeAsync(user, request.PlanHandle.Trim());
        response.Subscription = Map(subscription);

        return Results.Created($"api/my-subscriptions/{response.Subscription.Id}", response);
    }

    internal static async Task<ApplicationUser?> ResolveUserAsync(UserManager<ApplicationUser> userManager, ClaimsPrincipal principal)
    {
        var userName = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        return await userManager.FindByNameAsync(userName);
    }

    internal static SubscriptionDto Map(MaxioSubscriptionSummary subscription) => new()
    {
        Id = subscription.SubscriptionId,
        State = subscription.State ?? string.Empty,
        IsLive = subscription.IsLive,
        PlanHandle = subscription.ProductHandle ?? string.Empty,
        PlanName = subscription.ProductName ?? string.Empty,
        PriceInCents = subscription.PriceInCents,
        Price = subscription.PriceInCents.HasValue
            ? ListSubscriptionPlansEndpoint.PriceFromCents(subscription.PriceInCents.Value)
            : null,
        NextBillingDate = subscription.NextBillingDate,
        ActivatedAt = subscription.ActivatedAt,
        CreatedAt = subscription.CreatedAt,
        CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod,
        MaxioCustomerId = subscription.CustomerId
    };
}