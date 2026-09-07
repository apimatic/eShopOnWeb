using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.eShopWeb.PublicApi.Services;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[Authorize]
[Route("api/my-subscriptions")]
[ApiController]
public class ListUserSubscriptionsEndpoint : ControllerBase
{
    private readonly MaxioSubscriptionService _subscriptionService;
    private readonly ILogger<ListUserSubscriptionsEndpoint> _logger;

    public ListUserSubscriptionsEndpoint(
        MaxioSubscriptionService subscriptionService,
        ILogger<ListUserSubscriptionsEndpoint> logger)
    {
        _subscriptionService = subscriptionService;
        _logger = logger;
    }

    [HttpGet]
    [SwaggerOperation(
        Summary = "Get user's subscriptions",
        Description = "Returns the authenticated user's active subscriptions",
        OperationId = "subscriptions.list_user",
        Tags = new[] { "SubscriptionEndpoints" })]
    public async Task<ActionResult<ListSubscriptionsResponse>> HandleAsync(CancellationToken ct)
    {
        var response = new ListSubscriptionsResponse(Guid.NewGuid());

        try
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(new { message = "User ID not found in token" });
            }

            var subscriptions = await _subscriptionService.GetUserSubscriptionsAsync(userId, ct);
            response.Subscriptions.AddRange(subscriptions);
            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Failed to fetch user subscriptions");
            return StatusCode(400, new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error fetching subscriptions");
            return StatusCode(500, new { message = "An unexpected error occurred while fetching subscriptions" });
        }
    }
}
