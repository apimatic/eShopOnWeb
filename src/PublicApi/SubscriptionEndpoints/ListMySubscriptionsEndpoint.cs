using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.eShopWeb.PublicApi.Services;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[Authorize]
public sealed class ListMySubscriptionsEndpoint : EndpointBaseAsync.WithoutRequest.WithActionResult<ListMySubscriptionsResponse>
{
    private readonly MaxioSubscriptionService _service;
    private readonly ILogger<ListMySubscriptionsEndpoint> _logger;

    public ListMySubscriptionsEndpoint(MaxioSubscriptionService service, ILogger<ListMySubscriptionsEndpoint> logger)
    {
        _service = service;
        _logger = logger;
    }

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "Lists the authenticated user's subscriptions",
        Description = "Gets all subscriptions for the authenticated user",
        OperationId = "subscriptions.list-my",
        Tags = new[] { "Subscriptions" })]
    [ProducesResponseType(typeof(ListMySubscriptionsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public override async Task<ActionResult<ListMySubscriptionsResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? throw new UnauthorizedAccessException("User ID not found in token");

        try
        {
            var subscriptions = await _service.ListCustomerSubscriptionsAsync(userId, cancellationToken);
            var responses = subscriptions.Select(s => new SubscriptionResponse
            {
                Id = s.Id,
                MaxioSubscriptionId = s.MaxioSubscriptionId,
                ProductHandle = s.ProductHandle,
                Status = s.Status,
                PriceInCents = s.PriceInCents,
                CurrentPeriodEndsAt = s.CurrentPeriodEndsAt
            });

            return Ok(new ListMySubscriptionsResponse(responses));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list subscriptions for user {UserId}", userId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to list subscriptions" });
        }
    }
}

public sealed class SubscriptionResponse
{
    public int Id { get; set; }
    public int MaxioSubscriptionId { get; set; }
    public string ProductHandle { get; set; } = null!;
    public string Status { get; set; } = null!;
    public long PriceInCents { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
}

public sealed class ListMySubscriptionsResponse
{
    public ListMySubscriptionsResponse(IEnumerable<SubscriptionResponse> subscriptions)
    {
        Subscriptions = subscriptions;
    }

    public IEnumerable<SubscriptionResponse> Subscriptions { get; }
}
