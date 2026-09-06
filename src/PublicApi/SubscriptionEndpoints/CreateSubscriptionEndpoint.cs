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

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest>
{
    private readonly MaxioSubscriptionService _subscriptionService;
    private readonly ILogger<CreateSubscriptionEndpoint> _logger;

    public CreateSubscriptionEndpoint(MaxioSubscriptionService subscriptionService, ILogger<CreateSubscriptionEndpoint> logger)
    {
        _subscriptionService = subscriptionService;
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (ClaimsPrincipal user, int productId, CancellationToken ct) =>
            {
                var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var email = user.FindFirst(ClaimTypes.Email)?.Value;

                var request = new CreateSubscriptionRequest
                {
                    ProductId = productId,
                    UserId = userId,
                    Email = email
                };

                return await HandleAsync(request);
            })
            .RequireAuthorization("Bearer")
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .WithName("CreateSubscription")
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        if (string.IsNullOrEmpty(request.UserId))
        {
            _logger.LogWarning("No user ID found in JWT token");
            return Results.Unauthorized();
        }

        try
        {
            // Ensure customer exists (idempotent)
            var customerId = await _subscriptionService.EnsureCustomerExistsAsync(request.UserId, request.Email);

            // Create subscription
            var subscription = await _subscriptionService.CreateSubscriptionAsync(customerId, request.ProductId, request.UserId);

            response = new CreateSubscriptionResponse(request.CorrelationId())
            {
                Subscription = subscription
            };

            return Results.Created($"/api/subscriptions/{subscription.Id}", response);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Error creating subscription for user {UserId}", request.UserId);
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error creating subscription for user {UserId}", request.UserId);
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
}
