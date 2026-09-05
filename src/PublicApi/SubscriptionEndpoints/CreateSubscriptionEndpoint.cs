using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest>
{
    private readonly IMaxioSubscriptionService _maxioService;
    private readonly ILogger<CreateSubscriptionEndpoint> _logger;

    public CreateSubscriptionEndpoint(
        IMaxioSubscriptionService maxioService,
        ILogger<CreateSubscriptionEndpoint> logger)
    {
        _maxioService = maxioService;
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions", Handle)
            .RequireAuthorization()
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status500InternalServerError)
            .WithName("CreateSubscription")
            .WithTags("SubscriptionEndpoints")
            .WithOpenApi();
    }

    private async Task<IResult> Handle(CreateSubscriptionRequest request, HttpContext context)
    {
        try
        {
            var ct = context.RequestAborted;
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(userId))
            {
                _logger.LogWarning("CreateSubscriptionEndpoint called without user identity");
                return Results.Unauthorized();
            }

            if (string.IsNullOrEmpty(request.ProductHandle))
            {
                return Results.BadRequest(new ErrorResponse { Message = "ProductHandle is required" });
            }

            var userEmail = context.User.FindFirst(ClaimTypes.Email)?.Value ?? userId + "@eshop.local";
            var userFirstName = context.User.FindFirst(ClaimTypes.GivenName)?.Value ?? "User";
            var userLastName = context.User.FindFirst(ClaimTypes.Surname)?.Value ?? userId;

            _logger.LogInformation("Creating subscription for user {UserId} with product {ProductHandle}", userId, request.ProductHandle);

            var customer = await _maxioService.GetOrCreateCustomerAsync(
                userId,
                userEmail,
                userFirstName,
                userLastName,
                ct);

            var subscription = await _maxioService.CreateSubscriptionAsync(
                customer.Id,
                request.ProductHandle,
                ct);

            var response = new CreateSubscriptionResponse
            {
                SubscriptionId = subscription.Id,
                State = subscription.State,
                ProductHandle = subscription.ProductHandle,
                NextBillingAt = subscription.NextBillingAt,
                CreatedAt = subscription.CreatedAt
            };

            return Results.Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Invalid operation creating subscription");
            return Results.BadRequest(new ErrorResponse { Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating subscription");
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
}

public class CreateSubscriptionRequest
{
    public string? ProductHandle { get; set; }
}

public class CreateSubscriptionResponse
{
    public int SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public string? ProductHandle { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
}

public class ErrorResponse
{
    public string Message { get; set; } = string.Empty;
}
