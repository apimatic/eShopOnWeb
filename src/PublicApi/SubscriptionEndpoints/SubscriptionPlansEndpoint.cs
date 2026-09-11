using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Services;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[Authorize]
public class SubscriptionPlansEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<SubscriptionPlansResponse>
{
    private readonly IMaxioService _maxio;
    public SubscriptionPlansEndpoint(IMaxioService maxio) => _maxio = maxio;

    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(Summary = "List available subscription plans", Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<SubscriptionPlansResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var plans = await _maxio.GetPlansAsync(cancellationToken);
        return new SubscriptionPlansResponse
        {
            Plans = plans.Select(p => new PlanItem
            {
                Handle = p.Handle,
                Name = p.Name,
                Price = p.Price,
                Interval = p.Interval
            }).ToList()
        };
    }
}
