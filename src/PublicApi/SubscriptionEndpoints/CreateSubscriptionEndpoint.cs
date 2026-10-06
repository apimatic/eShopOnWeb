using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a plan. Idempotent: repeating the call
/// with the same plan returns the existing subscription instead of creating a
/// duplicate.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionService>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(UserManager<ApplicationUser> userManager, IHttpContextAccessor httpContextAccessor)
    {
        _userManager = userManager;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionService subscriptionService)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var userName = _httpContextAccessor.HttpContext?.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Results.Unauthorized();
        }

        var applicationUser = await _userManager.FindByNameAsync(userName);
        if (applicationUser is null)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest(new { message = "A planHandle is required." });
        }

        var result = await subscriptionService.SubscribeAsync(
            request.PlanHandle,
            applicationUser.Id,
            applicationUser.UserName ?? userName,
            applicationUser.Email ?? userName);

        response.Created = result.Created;
        response.Subscription = new SubscriptionDto
        {
            SubscriptionId = result.Subscription.SubscriptionId,
            Reference = result.Subscription.Reference,
            PlanHandle = result.Subscription.PlanHandle,
            PlanName = result.Subscription.PlanName,
            Price = result.Subscription.Price,
            State = result.Subscription.State,
            NextBillingAt = result.Subscription.NextBillingAt,
            CurrentPeriodEndsAt = result.Subscription.CurrentPeriodEndsAt,
            ActivatedAt = result.Subscription.ActivatedAt,
            CanceledAt = result.Subscription.CanceledAt
        };

        return result.Created
            ? Results.Created($"api/my-subscriptions/{result.Subscription.SubscriptionId}", response)
            : Results.Ok(response);
    }
}