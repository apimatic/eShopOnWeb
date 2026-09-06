using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsListEndpoint : IEndpoint<IResult, ListMySubscriptionsRequest>
{
    private readonly MaxioSubscriptionService _subscriptionService;
    private readonly ILogger<MySubscriptionsListEndpoint> _logger;

    public MySubscriptionsListEndpoint(MaxioSubscriptionService subscriptionService, ILogger<MySubscriptionsListEndpoint> logger)
    {
        _subscriptionService = subscriptionService;
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (ClaimsPrincipal user) =>
            {
                var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var email = user.FindFirst(ClaimTypes.Email)?.Value;

                var request = new ListMySubscriptionsRequest
                {
                    UserId = userId,
                    Email = email
                };

                return await HandleAsync(request);
            })
            .RequireAuthorization("Bearer")
            .Produces<ListMySubscriptionsResponse>()
            .WithName("ListMySubscriptions")
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListMySubscriptionsRequest request)
    {
        var response = new ListMySubscriptionsResponse(request.CorrelationId());

        if (string.IsNullOrEmpty(request.UserId))
        {
            _logger.LogWarning("No user ID found in JWT token");
            return Results.Unauthorized();
        }

        try
        {
            // Ensure customer exists (idempotent)
            var customerId = await _subscriptionService.EnsureCustomerExistsAsync(request.UserId, request.Email);

            // Get customer's subscriptions
            var subscriptions = await _subscriptionService.ListCustomerSubscriptionsAsync(customerId);
            response.Subscriptions.AddRange(subscriptions);

            return Results.Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Error retrieving subscriptions for user {UserId}", request.UserId);
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error retrieving subscriptions for user {UserId}", request.UserId);
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
}
