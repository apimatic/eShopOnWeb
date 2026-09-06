using System;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest>
{
    private readonly IMaxioBillingService _billingService;
    private readonly ILogger<CreateSubscriptionEndpoint> _logger;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(
        IMaxioBillingService billingService,
        ILogger<CreateSubscriptionEndpoint> logger,
        IHttpContextAccessor httpContextAccessor)
    {
        _billingService = billingService;
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
        try
        {
            // Extract user identity from JWT token
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext == null)
            {
                _logger.LogError("HttpContext is null");
                return Results.Unauthorized();
            }

            var user = httpContext.User;
            var email = user.FindFirst(ClaimTypes.Email)?.Value;
            var givenName = user.FindFirst(ClaimTypes.GivenName)?.Value ?? "User";
            var surname = user.FindFirst(ClaimTypes.Surname)?.Value ?? "";
            var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(email))
            {
                _logger.LogWarning("Subscription request with missing email claim");
                return Results.Unauthorized();
            }

            _logger.LogInformation("Processing subscription request for user {UserId} with email {Email}", userId, email);

            // Get or create Maxio customer (idempotent)
            var customer = await _billingService.GetOrCreateCustomerAsync(givenName, surname, email);

            // Create subscription
            var subscription = await _billingService.CreateSubscriptionAsync(customer.Id, request.ProductId);

            // Get full subscription details
            var subscriptionDetails = await _billingService.GetSubscriptionAsync(subscription.Id);

            var response = new CreateSubscriptionResponse
            {
                Subscription = new SubscriptionDetailDto
                {
                    Id = subscriptionDetails.Id,
                    State = subscriptionDetails.State,
                    ProductName = subscriptionDetails.ProductName,
                    ProductHandle = subscriptionDetails.ProductHandle,
                    NextBillingDate = subscriptionDetails.NextAssessmentAt,
                    CurrentPeriodStartsAt = subscriptionDetails.CurrentPeriodStartsAt,
                    CurrentPeriodEndsAt = subscriptionDetails.CurrentPeriodEndsAt,
                    CreatedAt = subscriptionDetails.CreatedAt
                }
            };

            _logger.LogInformation("Successfully created subscription {SubscriptionId} for user {UserId}", subscription.Id, userId);

            return Results.Created($"/api/subscriptions/{subscription.Id}", response);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Maxio API error creating subscription");
            return Results.BadRequest(new { error = "Failed to create subscription", detail = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error creating subscription");
            return Results.StatusCode(500);
        }
    }
}

public class CreateSubscriptionRequest
{
    public long ProductId { get; set; }
}

public class SubscriptionDetailDto
{
    public long Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public DateTime? NextBillingDate { get; set; }
    public DateTime? CurrentPeriodStartsAt { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreateSubscriptionResponse
{
    public SubscriptionDetailDto? Subscription { get; set; }
}
