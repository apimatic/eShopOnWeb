using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Services;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListUserSubscriptionsEndpoint : IEndpoint<IResult>
{
    private readonly IMaxioService _maxioService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<ListUserSubscriptionsEndpoint> _logger;

    public ListUserSubscriptionsEndpoint(
        IMaxioService maxioService,
        UserManager<ApplicationUser> userManager,
        ILogger<ListUserSubscriptionsEndpoint> logger)
    {
        _maxioService = maxioService;
        _userManager = userManager;
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (ClaimsPrincipal user) =>
            {
                return await HandleAsync(user);
            })
           .Produces<ListSubscriptionsResponse>()
           .Produces(StatusCodes.Status401Unauthorized)
           .WithTags("SubscriptionEndpoints")
           .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync()
    {
        throw new NotImplementedException("Use MapGet handler instead");
    }

    private async Task<IResult> HandleAsync(ClaimsPrincipal user)
    {
        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("Unable to extract user ID from JWT token");
            return Results.Unauthorized();
        }

        var appUser = await _userManager.FindByIdAsync(userId);
        if (appUser == null)
        {
            _logger.LogWarning("User {UserId} not found", userId);
            return Results.Unauthorized();
        }

        _logger.LogInformation("Fetching subscriptions for user {UserId}", userId);

        var subscriptions = await _maxioService.GetUserSubscriptionsAsync(appUser);
        var response = new ListSubscriptionsResponse
        {
            Subscriptions = subscriptions.Select(MapToDto).ToList()
        };

        return Results.Ok(response);
    }

    private SubscriptionDto MapToDto(Services.SubscriptionData subscription)
    {
        return new SubscriptionDto
        {
            Id = subscription.id,
            State = subscription.state,
            CustomerId = subscription.customer_id,
            ActivatedAt = subscription.activated_at,
            CanceledAt = subscription.canceled_at,
            CurrentPeriodStartsAt = subscription.current_period_starts_at,
            CurrentPeriodEndsAt = subscription.current_period_ends_at,
            NextAssessmentAt = subscription.next_assessment_at,
            ProductPricePerMonth = subscription.product_price_in_cents / 100m,
            ProductName = subscription.product.name,
            ProductHandle = subscription.product.handle
        };
    }
}
