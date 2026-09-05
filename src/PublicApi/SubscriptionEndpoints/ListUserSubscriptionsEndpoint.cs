using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Logging;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[Authorize]
public class ListUserSubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<ListUserSubscriptionsResponse>
{
    private readonly IMaxioService _maxioService;
    private readonly IReadRepository<UserSubscription> _subscriptionRepository;
    private readonly ILogger<ListUserSubscriptionsEndpoint> _logger;

    public ListUserSubscriptionsEndpoint(
        IMaxioService maxioService,
        IReadRepository<UserSubscription> subscriptionRepository,
        ILogger<ListUserSubscriptionsEndpoint> logger)
    {
        _maxioService = maxioService;
        _subscriptionRepository = subscriptionRepository;
        _logger = logger;
    }

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "List user's subscriptions",
        Description = "Returns all subscriptions for the authenticated user",
        OperationId = "subscriptions.listMine",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<ListUserSubscriptionsResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var response = new ListUserSubscriptionsResponse();

        try
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var spec = new UserSubscriptionsByUserSpecification(userId);
            var userSubscriptions = await _subscriptionRepository.ListAsync(spec, cancellationToken);

            response.Subscriptions = userSubscriptions.Select(s => new UserSubscriptionDto
            {
                MaxioSubscriptionId = s.MaxioSubscriptionId,
                MaxioCustomerId = s.MaxioCustomerId,
                State = s.State,
                CurrentPeriodStartsAt = s.CurrentPeriodStartAt,
                CurrentPeriodEndsAt = s.CurrentPeriodEndAt,
                CreatedAt = s.CreatedAt,
                UpdatedAt = s.UpdatedAt
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing user subscriptions");
            return StatusCode(500, new { error = "Failed to load subscriptions" });
        }

        return Ok(response);
    }
}

public class ListUserSubscriptionsResponse
{
    public List<UserSubscriptionDto> Subscriptions { get; set; } = new();
}

public class UserSubscriptionDto
{
    public int MaxioSubscriptionId { get; set; }
    public int MaxioCustomerId { get; set; }
    public required string State { get; set; }
    public DateTime? CurrentPeriodStartsAt { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

