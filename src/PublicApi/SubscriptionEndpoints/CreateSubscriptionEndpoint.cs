using System;
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
public sealed class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<CreateSubscriptionResponse>
{
    private readonly MaxioSubscriptionService _service;
    private readonly ILogger<CreateSubscriptionEndpoint> _logger;

    public CreateSubscriptionEndpoint(MaxioSubscriptionService service, ILogger<CreateSubscriptionEndpoint> logger)
    {
        _service = service;
        _logger = logger;
    }

    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Creates a new subscription",
        Description = "Subscribes the authenticated user to a plan",
        OperationId = "subscriptions.create",
        Tags = new[] { "Subscriptions" })]
    [ProducesResponseType(typeof(CreateSubscriptionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(
        CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? throw new UnauthorizedAccessException("User ID not found in token");

        try
        {
            var subscription = await _service.SubscribeAsync(userId, request.ProductHandle, cancellationToken);

            return CreatedAtAction(nameof(CreateSubscriptionEndpoint), new CreateSubscriptionResponse
            {
                Id = subscription.Id,
                MaxioSubscriptionId = subscription.MaxioSubscriptionId,
                ProductHandle = subscription.ProductHandle,
                Status = subscription.Status,
                PriceInCents = subscription.PriceInCents,
                CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt
            });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Subscription creation failed for user {UserId}", userId);
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error creating subscription for user {UserId}", userId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Subscription creation failed" });
        }
    }
}

public sealed class CreateSubscriptionRequest
{
    public string ProductHandle { get; set; } = null!;
}

public sealed class CreateSubscriptionResponse
{
    public int Id { get; set; }
    public int MaxioSubscriptionId { get; set; }
    public string ProductHandle { get; set; } = null!;
    public string Status { get; set; } = null!;
    public long PriceInCents { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
}
