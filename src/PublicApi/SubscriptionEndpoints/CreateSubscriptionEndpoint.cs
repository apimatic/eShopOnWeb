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
[Route("api/subscriptions")]
[ApiController]
public class CreateSubscriptionEndpoint : ControllerBase
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

    [HttpPost]
    [SwaggerOperation(
        Summary = "Create a subscription",
        Description = "Creates a new subscription for the authenticated user",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })]
    public async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(
        [FromBody] CreateSubscriptionRequestDto request,
        CancellationToken ct)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        try
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(new { message = "User ID not found in token" });
            }

            var email = User.FindFirst(ClaimTypes.Email)?.Value ?? "unknown@example.com";
            var firstName = User.FindFirst("given_name")?.Value ?? "Customer";
            var lastName = User.FindFirst("family_name")?.Value ?? "User";

            var subscription = await _subscriptionService.CreateSubscriptionAsync(
                userId,
                request.PlanHandle,
                email,
                firstName,
                lastName,
                ct);

            response.Subscription = subscription;
            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Failed to create subscription for user");
            return StatusCode(400, new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error creating subscription");
            return StatusCode(500, new { message = "An unexpected error occurred while creating the subscription" });
        }
    }
}
