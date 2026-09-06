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

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest>
{
    private readonly IMaxioService _maxioService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<CreateSubscriptionEndpoint> _logger;

    public CreateSubscriptionEndpoint(
        IMaxioService maxioService,
        UserManager<ApplicationUser> userManager,
        ILogger<CreateSubscriptionEndpoint> logger)
    {
        _maxioService = maxioService;
        _userManager = userManager;
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (CreateSubscriptionRequest request, ClaimsPrincipal user) =>
            {
                return await HandleAsync(request, user);
            })
           .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
           .Produces(StatusCodes.Status400BadRequest)
           .Produces(StatusCodes.Status401Unauthorized)
           .WithTags("SubscriptionEndpoints")
           .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request)
    {
        throw new NotImplementedException("Use MapPost handler instead");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ClaimsPrincipal user)
    {
        if (string.IsNullOrEmpty(request.PlanHandle))
        {
            return Results.BadRequest(new { error = "PlanHandle is required" });
        }

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

        _logger.LogInformation("Creating subscription for user {UserId} with plan {Plan}", userId, request.PlanHandle);

        var subscription = await _maxioService.CreateSubscriptionAsync(appUser, request.PlanHandle);
        if (subscription == null)
        {
            return Results.BadRequest(new { error = "Failed to create subscription" });
        }

        var response = new CreateSubscriptionResponse
        {
            Subscription = MapToDto(subscription)
        };

        return Results.Created($"api/subscriptions/{subscription.id}", response);
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
