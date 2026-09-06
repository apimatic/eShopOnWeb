using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ListMySubscriptionsRequest>
{
    private readonly IMaxioSubscriptionService _subscriptionService;
    private readonly ILogger<ListMySubscriptionsEndpoint> _logger;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ListMySubscriptionsEndpoint(
        IMaxioSubscriptionService subscriptionService,
        ILogger<ListMySubscriptionsEndpoint> logger,
        IHttpContextAccessor httpContextAccessor)
    {
        _subscriptionService = subscriptionService;
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async () =>
            {
                return await HandleAsync(new ListMySubscriptionsRequest());
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("GetMySubscriptions");
    }

    public async Task<IResult> HandleAsync(ListMySubscriptionsRequest request)
    {
        var cancellationToken = CancellationToken.None;
        var response = new ListMySubscriptionsResponse(request.CorrelationId());

        try
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user == null)
            {
                _logger.LogWarning("ListMySubscriptions: No user context available");
                return Results.Unauthorized();
            }

            var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                _logger.LogWarning("ListMySubscriptions: User ID not found in token");
                return Results.Unauthorized();
            }

            var userEmail = user.FindFirst(ClaimTypes.Email)?.Value ?? string.Empty;

            _logger.LogInformation("Listing subscriptions for userId {UserId}", userId);

            var customerId = await _subscriptionService.EnsureCustomerExistsAsync(userId, userEmail, cancellationToken);
            if (customerId == 0)
            {
                _logger.LogInformation("No customer found for userId {UserId}, returning empty list", userId);
                return Results.Ok(response);
            }

            var subscriptions = await _subscriptionService.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
            response.Subscriptions = subscriptions;

            _logger.LogInformation("Retrieved {Count} subscriptions for userId {UserId}", subscriptions.Count, userId);
            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing subscriptions");
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}
