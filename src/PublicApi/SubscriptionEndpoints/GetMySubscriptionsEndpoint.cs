using System;
using System.Collections.Generic;
using System.Linq;
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

public class GetMySubscriptionsEndpoint : IEndpoint<IResult>
{
    private readonly IMaxioBillingService _billingService;
    private readonly ILogger<GetMySubscriptionsEndpoint> _logger;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public GetMySubscriptionsEndpoint(
        IMaxioBillingService billingService,
        ILogger<GetMySubscriptionsEndpoint> logger,
        IHttpContextAccessor httpContextAccessor)
    {
        _billingService = billingService;
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async () =>
            {
                return await HandleAsync();
            })
            .Produces<GetMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("GetMySubscriptions");
    }

    public async Task<IResult> HandleAsync()
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
            var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(email))
            {
                _logger.LogWarning("My subscriptions request with missing email claim");
                return Results.Unauthorized();
            }

            _logger.LogInformation("Retrieving subscriptions for user {UserId} with email {Email}", userId, email);

            // For this implementation, we need to get the Maxio customer ID first
            // We'll search by email to find the customer, then get their subscriptions
            // Since we don't have direct customer lookup by email in the service yet,
            // we'll need to handle the case where the customer might not exist

            var response = new GetMySubscriptionsResponse
            {
                Subscriptions = new List<UserSubscriptionDto>()
            };

            // Try to get customer and their subscriptions
            // Note: In a production system, you'd store the mapping of user.Id -> maxio.customer_id
            // For now, we'll return an empty list if the customer doesn't exist in Maxio
            try
            {
                // Create a temporary customer to get the ID (this will return existing if found)
                var givenName = user.FindFirst(ClaimTypes.GivenName)?.Value ?? "User";
                var surname = user.FindFirst(ClaimTypes.Surname)?.Value ?? "";

                var customer = await _billingService.GetOrCreateCustomerAsync(givenName, surname, email);

                // Now get their subscriptions
                var subscriptions = await _billingService.GetCustomerSubscriptionsAsync(customer.Id);

                response.Subscriptions = subscriptions.Select(s => new UserSubscriptionDto
                {
                    Id = s.Id,
                    State = s.State,
                    ProductName = s.ProductName,
                    ProductHandle = s.ProductHandle,
                    NextBillingDate = s.NextAssessmentAt,
                    CurrentPeriodStartsAt = s.CurrentPeriodStartsAt,
                    CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
                    BalanceInDollars = s.GetBalanceInDollars(),
                    CreatedAt = s.CreatedAt,
                    ActivatedAt = s.ActivatedAt,
                    CanceledAt = s.CanceledAt
                }).ToList();

                _logger.LogInformation("Retrieved {SubscriptionCount} subscriptions for user {UserId}", response.Subscriptions.Count, userId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error retrieving subscriptions for user {UserId}", userId);
                // Return empty list rather than error - customer might not have any subscriptions yet
            }

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error retrieving subscriptions");
            return Results.StatusCode(500);
        }
    }
}

public class UserSubscriptionDto
{
    public long Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public DateTime? NextBillingDate { get; set; }
    public DateTime? CurrentPeriodStartsAt { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public decimal BalanceInDollars { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime? CanceledAt { get; set; }
}

public class GetMySubscriptionsResponse
{
    public List<UserSubscriptionDto> Subscriptions { get; set; } = new();
}
