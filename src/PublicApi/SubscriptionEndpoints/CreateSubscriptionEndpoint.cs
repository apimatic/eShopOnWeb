using System;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Logging;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the signed-in shopper to a Maxio plan. Idempotent: subscribing to
/// a plan the shopper already has returns the existing subscription.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<CreateSubscriptionResponse>
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly ILogger<CreateSubscriptionEndpoint> _logger;

    public CreateSubscriptionEndpoint(ISubscriptionService subscriptionService, ILogger<CreateSubscriptionEndpoint> logger)
    {
        _subscriptionService = subscriptionService;
        _logger = logger;
    }

    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Subscribes the caller to a plan",
        Description = "Ensures a Maxio customer exists for the caller (idempotently) and creates a subscription " +
                      "to the requested plan, confirming plan, price, state and next billing date. Re-subscribing " +
                      "to a plan the caller already has returns the existing subscription.",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(
        CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return BadRequest(new { message = "A 'planHandle' is required." });
        }

        var shopper = BillingShopperFactory.FromPrincipal(User);
        if (shopper == null)
        {
            return Unauthorized();
        }

        try
        {
            var result = await _subscriptionService.SubscribeAsync(shopper, request.PlanHandle.Trim(), cancellationToken);
            response.Subscription = result.Subscription;
            response.Created = result.Created;
            return result.Created
                ? StatusCode(StatusCodes.Status201Created, response)
                : Ok(response);
        }
        catch (PlanNotFoundException ex)
        {
            _logger.LogInformation(ex, "Shopper {Reference} tried to subscribe to unknown plan {Plan}.", shopper.Reference, request.PlanHandle);
            return BadRequest(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (MaxioApiException ex)
        {
            _logger.LogError(ex, "Maxio request failed while subscribing {Reference} to plan {Plan}.", shopper.Reference, request.PlanHandle);
            if (ex.StatusCode == 422)
            {
                return BadRequest(new { message = "The subscription could not be created: " + ex.Message });
            }
            return StatusCode(502, new { message = "The billing provider could not be reached." });
        }
    }
}
