using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Logging;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans (from the configured Maxio product family)
/// that the signed-in shopper can subscribe to.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class ListSubscriptionPlansEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<ListSubscriptionPlansResponse>
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly ILogger<ListSubscriptionPlansEndpoint> _logger;

    public ListSubscriptionPlansEndpoint(ISubscriptionService subscriptionService, ILogger<ListSubscriptionPlansEndpoint> logger)
    {
        _subscriptionService = subscriptionService;
        _logger = logger;
    }

    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(
        Summary = "Lists available subscription plans",
        Description = "Lists the plans in the configured Maxio product family that the caller may subscribe to.",
        OperationId = "subscriptions.listPlans",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<ListSubscriptionPlansResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var response = new ListSubscriptionPlansResponse();

        try
        {
            var plans = await _subscriptionService.GetAvailablePlansAsync(cancellationToken);
            response.Plans.AddRange(plans);
            return Ok(response);
        }
        catch (MaxioApiException ex)
        {
            _logger.LogError(ex, "Maxio request failed while listing subscription plans.");
            return StatusCode(502, BillingError("The billing provider could not be reached."));
        }
    }

    private static object BillingError(string message)
    {
        return new { message };
    }
}
