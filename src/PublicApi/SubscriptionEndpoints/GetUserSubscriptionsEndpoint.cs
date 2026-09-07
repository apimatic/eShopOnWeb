using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi.Services;
using Microsoft.Extensions.Logging;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[Authorize]
public class GetUserSubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<GetUserSubscriptionsResponse>
{
    private readonly MaxioSubscriptionService _subscriptionService;
    private readonly ILogger<GetUserSubscriptionsEndpoint> _logger;

    public GetUserSubscriptionsEndpoint(
        MaxioSubscriptionService subscriptionService,
        ILogger<GetUserSubscriptionsEndpoint> logger)
    {
        _subscriptionService = subscriptionService;
        _logger = logger;
    }

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "Get user's subscriptions",
        Description = "Returns the authenticated user's active subscriptions",
        OperationId = "subscriptions.getUserSubscriptions",
        Tags = new[] { "Subscriptions" })
    ]
    public override async Task<ActionResult<GetUserSubscriptionsResponse>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var userEmail = User.FindFirst(ClaimTypes.Email)?.Value;

            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(userEmail))
            {
                _logger.LogWarning("Missing user identity information");
                return Unauthorized(new GetUserSubscriptionsResponse
                {
                    Result = false,
                    Error = "User identity not found"
                });
            }

            _logger.LogInformation("Fetching subscriptions for user {UserId}", userId);

            var maxioCustomerId = await _subscriptionService.EnsureMaxioCustomerAsync(
                userId, userEmail, cancellationToken);

            var subscriptions = await _subscriptionService.GetUserSubscriptionsAsync(
                maxioCustomerId, cancellationToken);

            return Ok(new GetUserSubscriptionsResponse
            {
                Result = true,
                Subscriptions = subscriptions.Select(s => new UserSubscriptionDto
                {
                    Id = s.Id,
                    ProductName = s.ProductName,
                    State = s.State,
                    PriceInCents = s.PriceInCents,
                    BalanceInCents = s.BalanceInCents,
                    NextBillingAt = s.NextAssessmentAt,
                    CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
                    ActivatedAt = s.ActivatedAt
                }).ToList()
            });
        }
        catch (MaxioServiceException ex)
        {
            _logger.LogError(ex, "Maxio service error fetching user subscriptions");
            return StatusCode(500, new GetUserSubscriptionsResponse
            {
                Result = false,
                Error = ex.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error fetching user subscriptions");
            return StatusCode(500, new GetUserSubscriptionsResponse
            {
                Result = false,
                Error = "Failed to retrieve subscriptions"
            });
        }
    }
}

public class GetUserSubscriptionsResponse : BaseResponse
{
    public bool Result { get; set; } = true;
    public List<UserSubscriptionDto> Subscriptions { get; set; } = new();
    public string? Error { get; set; }
}

public class UserSubscriptionDto
{
    public int Id { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public long BalanceInCents { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
}
