using System;
using System.Collections.Generic;
using System.Linq;
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
    private readonly IMaxioService _maxio;
    public GetSubscriptionPlansEndpoint(IMaxioService maxio) => _maxio = maxio;

    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(
        Summary = "List available subscription plans",
        Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<List<SubscriptionPlanResponse>>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var plans = await _maxio.GetPlansAsync(cancellationToken);
        return Ok(plans.Select(p => new SubscriptionPlanResponse
        {
            Id = p.Id,
            Handle = p.Handle,
            Name = p.Name,
            Price = p.PriceInCents / 100m,
            Interval = p.Interval
        }).ToList());
    }
}
