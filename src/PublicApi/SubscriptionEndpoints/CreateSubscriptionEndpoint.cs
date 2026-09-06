using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, SubscribeRequest>
{
    private readonly MaxioSubscriptionService _subscriptionService;
    private readonly ILogger<CreateSubscriptionEndpoint> _logger;

    public CreateSubscriptionEndpoint(
        MaxioSubscriptionService subscriptionService,
        ILogger<CreateSubscriptionEndpoint> logger)
    {
        _subscriptionService = subscriptionService;
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (SubscribeRequest request, HttpContext httpContext, CancellationToken ct) =>
            {
                return await HandleAsyncInternal(request, httpContext, ct);
            })
            .Produces<SubscribeResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("CreateSubscription");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request)
    {
        throw new NotImplementedException();
    }

    private async Task<IResult> HandleAsyncInternal(
        SubscribeRequest request,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var response = new SubscribeResponse(request.CorrelationId());

        try
        {
            // Extract user identity from JWT claims
            var userEmail = httpContext.User.FindFirst(ClaimTypes.Email)?.Value
                ?? httpContext.User.FindFirst("email")?.Value;

            var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? httpContext.User.FindFirst("sub")?.Value;

            if (string.IsNullOrEmpty(userEmail) || string.IsNullOrEmpty(userId))
            {
                _logger.LogWarning("Missing user identity in JWT claims");
                response.ErrorMessage = "User identity not found in token";
                return Results.Unauthorized();
            }

            if (string.IsNullOrEmpty(request.PlanHandle))
            {
                response.ErrorMessage = "Plan handle is required";
                return Results.BadRequest(response);
            }

            _logger.LogInformation("Creating subscription for user {UserId} ({Email}) on plan {PlanHandle}",
                userId, userEmail, request.PlanHandle);

            // Verify plan exists
            var plan = await _subscriptionService.GetPlanByHandle(request.PlanHandle, ct);
            if (plan == null)
            {
                response.ErrorMessage = $"Plan '{request.PlanHandle}' not found";
                return Results.NotFound(response);
            }

            // Get or create Maxio customer
            var customerId = await _subscriptionService.GetOrCreateCustomer(userEmail, userId, ct);

            // Create subscription
            var subscription = await _subscriptionService.CreateSubscription(customerId, request.PlanHandle, ct);

            response.Subscription = subscription;
            response.Success = true;

            _logger.LogInformation("Successfully created subscription {SubscriptionId} for user {UserId}",
                subscription.Id, userId);

            return Results.Created($"api/subscriptions/{subscription.Id}", response);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Invalid operation creating subscription");
            response.ErrorMessage = ex.Message;
            return Results.BadRequest(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error creating subscription");
            response.ErrorMessage = "An unexpected error occurred while creating the subscription";
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
}
