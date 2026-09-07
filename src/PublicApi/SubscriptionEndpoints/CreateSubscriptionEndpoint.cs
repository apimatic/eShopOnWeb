using System;
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
public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<CreateSubscriptionResponse>
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

    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Create a subscription",
        Description = "Subscribe the authenticated user to a plan",
        OperationId = "subscriptions.create",
        Tags = new[] { "Subscriptions" })
    ]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(
        CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var userEmail = User.FindFirst(ClaimTypes.Email)?.Value;

            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(userEmail))
            {
                _logger.LogWarning("Missing user identity information");
                return Unauthorized(new CreateSubscriptionResponse
                {
                    Result = false,
                    Error = "User identity not found"
                });
            }

            if (string.IsNullOrEmpty(request.ProductHandle))
            {
                return BadRequest(new CreateSubscriptionResponse
                {
                    Result = false,
                    Error = "ProductHandle is required"
                });
            }

            _logger.LogInformation("Creating subscription for user {UserId} on product {ProductHandle}",
                userId, request.ProductHandle);

            var maxioCustomerId = await _subscriptionService.EnsureMaxioCustomerAsync(
                userId, userEmail, cancellationToken);

            var subscription = await _subscriptionService.CreateSubscriptionAsync(
                maxioCustomerId,
                userEmail,
                request.ProductHandle,
                cancellationToken);

            return Ok(new CreateSubscriptionResponse
            {
                Result = true,
                Subscription = new SubscriptionDetailsResponse
                {
                    Id = subscription.Id,
                    ProductName = subscription.ProductName,
                    State = subscription.State,
                    PriceInCents = subscription.PriceInCents,
                    NextBillingAt = subscription.NextAssessmentAt,
                    ActivatedAt = subscription.ActivatedAt
                }
            });
        }
        catch (MaxioServiceException ex)
        {
            _logger.LogError(ex, "Maxio service error during subscription creation");
            return StatusCode(422, new CreateSubscriptionResponse
            {
                Result = false,
                Error = ex.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during subscription creation");
            return StatusCode(500, new CreateSubscriptionResponse
            {
                Result = false,
                Error = "Failed to create subscription"
            });
        }
    }
}

public class CreateSubscriptionRequest
{
    public string ProductHandle { get; set; } = string.Empty;
}

public class CreateSubscriptionResponse : BaseResponse
{
    public bool Result { get; set; } = true;
    public SubscriptionDetailsResponse? Subscription { get; set; }
    public string? Error { get; set; }
}

public class SubscriptionDetailsResponse
{
    public int Id { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
}
