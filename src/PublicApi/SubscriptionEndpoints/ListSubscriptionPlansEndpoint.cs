using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[Authorize]
public class ListSubscriptionPlansEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<IEnumerable<SubscriptionPlanDto>>
{
    private readonly ISubscriptionService _subscriptionService;

    public ListSubscriptionPlansEndpoint(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(
        Summary = "List available subscription plans",
        Description = "Retrieves all available subscription plans from Maxio",
        OperationId = "subscriptions.list-plans",
        Tags = new[] { "Subscriptions" })]
    public override async Task<ActionResult<IEnumerable<SubscriptionPlanDto>>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        var plans = await _subscriptionService.GetSubscriptionPlansAsync(cancellationToken);
        return Ok(plans);
    }
}
