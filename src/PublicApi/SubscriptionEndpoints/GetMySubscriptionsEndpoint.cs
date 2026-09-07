using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetMySubscriptionsEndpoint : IEndpoint
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<GetMySubscriptionsEndpoint> _logger;

    public GetMySubscriptionsEndpoint(
        ISubscriptionService subscriptionService,
        UserManager<ApplicationUser> userManager,
        ILogger<GetMySubscriptionsEndpoint> logger)
    {
        _subscriptionService = subscriptionService;
        _userManager = userManager;
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext httpContext) =>
            {
                return await HandleAsync(httpContext);
            })
            .Produces<GetMySubscriptionsResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("SubscriptionEndpoints");
    }

    private async Task<IResult> HandleAsync(HttpContext httpContext)
    {
        try
        {
            var username = httpContext.User.FindFirst(ClaimTypes.Name)?.Value;
            if (string.IsNullOrEmpty(username))
            {
                _logger.LogWarning("No username found in JWT claims");
                return Results.Unauthorized();
            }

            var user = await _userManager.FindByNameAsync(username);
            if (user == null)
            {
                _logger.LogWarning("User not found: {Username}", username);
                return Results.Unauthorized();
            }

            var result = await _subscriptionService.GetUserSubscriptionsAsync(user.Id);

            if (!result.IsSuccess)
            {
                _logger.LogWarning("Failed to get subscriptions: {Error}", result.Errors.FirstOrDefault());
                return Results.BadRequest(new { error = result.Errors.FirstOrDefault() });
            }

            var response = new GetMySubscriptionsResponse
            {
                Subscriptions = result.Value.Select(s => new SubscriptionResponseDto
                {
                    Id = s.Id ?? 0,
                    MaxioSubscriptionId = s.MaxioSubscriptionId,
                    ProductHandle = s.ProductHandle,
                    ProductName = s.ProductName,
                    State = s.State,
                    PriceInCents = s.PriceInCents,
                    NextBillingAt = s.NextBillingAt,
                    CreatedAt = s.CreatedAt
                }).ToList()
            };

            _logger.LogInformation("Retrieved {Count} subscriptions for user {UserId}", response.Subscriptions.Count, user.Id);
            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get subscriptions");
            return Results.BadRequest(new { error = "Failed to retrieve subscriptions" });
        }
    }
}

public class GetMySubscriptionsResponse
{
    public List<SubscriptionResponseDto> Subscriptions { get; set; } = new();
}
