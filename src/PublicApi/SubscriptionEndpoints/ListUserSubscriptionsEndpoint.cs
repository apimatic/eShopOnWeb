using System;
using System.Collections.Generic;
using System.Linq;
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
public class ListUserSubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<ListUserSubscriptionsResponse>
{
    private readonly IMaxioSubscriptionService _subscriptionService;
    private readonly ILogger<ListUserSubscriptionsEndpoint> _logger;

    public ListUserSubscriptionsEndpoint(
        IMaxioSubscriptionService subscriptionService,
        ILogger<ListUserSubscriptionsEndpoint> logger)
    {
        _subscriptionService = subscriptionService;
        _logger = logger;
    }

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "List user's subscriptions",
        Description = "Returns all subscriptions for the authenticated user",
        OperationId = "subscriptions.listMine",
        Tags = new[] { "Subscriptions" })]
    public override async Task<ActionResult<ListUserSubscriptionsResponse>> HandleAsync(
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
            var subscriptions = await _subscriptionService.GetUserSubscriptionsAsync(userId, cancellationToken);

            var response = subscriptions.Select(s => new UserSubscriptionDto(
                SubscriptionId: s.Id,
                State: s.State,
                PlanHandle: s.ProductHandle,
                Price: s.ProductPriceInCents / 100m,
                NextBillingDate: s.NextBillingAt,
                ActivatedAt: s.ActivatedAt)).ToList();

            return Ok(new ListUserSubscriptionsResponse(response));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching subscriptions for user {UserId}", userId);
            return BadRequest(new { error = "Failed to fetch subscriptions" });
        }
    }
}

public record UserSubscriptionDto(
    int SubscriptionId,
    string State,
    string? PlanHandle,
    decimal Price,
    DateTime? NextBillingDate,
    DateTime? ActivatedAt);

public record ListUserSubscriptionsResponse(List<UserSubscriptionDto> Subscriptions);
