using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi.Services;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[Authorize]
public class GetSubscriptionPlansEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<List<SubscriptionPlanResponse>>
{
    private readonly MaxioService _maxio;
    public GetSubscriptionPlansEndpoint(MaxioService maxio) => _maxio = maxio;

    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(Summary = "List subscription plans", Tags = new[] { "Subscriptions" })]
    public override async Task<ActionResult<List<SubscriptionPlanResponse>>> HandleAsync(CancellationToken cancellationToken = default)
    {
        // Simplified: return hard-derived plans from sandbox; full SDK integration reads family
        var plans = new List<SubscriptionPlanResponse>
        {
            new() { Handle = "eshop-pro", Name = "Pro Plan", Price = 299.00, Currency = "USD" },
            new() { Handle = "basic-plan", Name = "Basic Plan", Price = 29.00, Currency = "USD" }
        };
        return Ok(plans);
    }
}
