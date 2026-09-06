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

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest>
{
    private readonly IMaxioSubscriptionService _subscriptionService;
    private readonly ILogger<CreateSubscriptionEndpoint> _logger;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(
        IMaxioSubscriptionService subscriptionService,
        ILogger<CreateSubscriptionEndpoint> logger,
        IHttpContextAccessor httpContextAccessor)
    {
        _subscriptionService = subscriptionService;
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request) =>
            {
                return await HandleAsync(request);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("CreateSubscription");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request)
    {
        var cancellationToken = CancellationToken.None;
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        try
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user == null)
            {
                _logger.LogWarning("CreateSubscription: No user context available");
                return Results.Unauthorized();
            }

            var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var userEmail = user.FindFirst(ClaimTypes.Email)?.Value;

            if (string.IsNullOrEmpty(userId))
            {
                _logger.LogWarning("CreateSubscription: User ID not found in token");
                return Results.Unauthorized();
            }

            if (string.IsNullOrEmpty(userEmail))
            {
                _logger.LogWarning("CreateSubscription: User email not found in token");
                return Results.BadRequest(new { error = "User email is required" });
            }

            _logger.LogInformation("Creating subscription for userId {UserId}, planHandle {PlanHandle}", userId, request.PlanHandle);

            var customerId = await _subscriptionService.EnsureCustomerExistsAsync(userId, userEmail, cancellationToken);
            if (customerId == 0)
            {
                _logger.LogError("Failed to create or find customer for userId {UserId}", userId);
                return Results.BadRequest(new { error = "Failed to create or find customer" });
            }

            var subscriptionReference = $"{userId}-{request.PlanHandle}-{Guid.NewGuid():N}";
            var subscription = await _subscriptionService.CreateSubscriptionAsync(customerId, request.PlanHandle, subscriptionReference, cancellationToken);

            response.Subscription = subscription;
            _logger.LogInformation("Successfully created subscription {SubscriptionId} for userId {UserId}", subscription.Id, userId);
            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating subscription");
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}
