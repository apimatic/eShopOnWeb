using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.eShopWeb.PublicApi.Services;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[Authorize]
[Route("api/subscription-plans")]
[ApiController]
public class ListSubscriptionPlansEndpoint : ControllerBase
{
    private readonly MaxioSubscriptionService _subscriptionService;

    public ListSubscriptionPlansEndpoint(MaxioSubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    [HttpGet]
    [SwaggerOperation(
        Summary = "List available subscription plans",
        Description = "Returns a list of available subscription plans from Maxio",
        OperationId = "subscriptions.list_plans",
        Tags = new[] { "SubscriptionEndpoints" })]
    public async Task<ActionResult<ListSubscriptionPlansResponse>> HandleAsync(CancellationToken ct)
    {
        var response = new ListSubscriptionPlansResponse(Guid.NewGuid());

        try
        {
            var plans = await _subscriptionService.GetSubscriptionPlansAsync(ct);
            response.Plans.AddRange(plans);
            return Ok(response);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Failed to retrieve subscription plans" });
        }
    }
}
