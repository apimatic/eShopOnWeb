using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[Authorize]
public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<CreateSubscriptionResponse>
{
    private readonly IMaxioSubscriptionService _subscriptionService;
    private readonly ILogger<CreateSubscriptionEndpoint> _logger;

    public CreateSubscriptionEndpoint(
        IMaxioSubscriptionService subscriptionService,
        ILogger<CreateSubscriptionEndpoint> logger)
    {
        _subscriptionService = subscriptionService;
        _logger = logger;
    }

    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Subscribe to a plan",
        Description = "Subscribe the authenticated user to a subscription plan",
        OperationId = "subscriptions.create",
        Tags = new[] { "Subscriptions" })]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(
        CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("Subscription request without user ID");
            return Unauthorized();
        }

        try
        {
            var subscription = await _subscriptionService.SubscribeAsync(
                userId, request.PlanHandle, cancellationToken);

            return Ok(new CreateSubscriptionResponse(
                SubscriptionId: subscription.Id,
                State: subscription.State,
                PlanHandle: subscription.ProductHandle,
                Price: subscription.ProductPriceInCents / 100m,
                NextBillingDate: subscription.NextBillingAt,
                ActivatedAt: subscription.ActivatedAt));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating subscription for user {UserId}", userId);
            return BadRequest(new { error = "Failed to create subscription" });
        }
    }
}

public record CreateSubscriptionRequest(string PlanHandle);

public record CreateSubscriptionResponse(
    int SubscriptionId,
    string State,
    string? PlanHandle,
    decimal Price,
    DateTime? NextBillingDate,
    DateTime? ActivatedAt);
