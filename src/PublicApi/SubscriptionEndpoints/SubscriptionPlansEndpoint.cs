using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlansEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithResult<ActionResult<List<SubscriptionPlanResponse>>>
{
    private readonly MaxioService _maxio;

    public SubscriptionPlansEndpoint(MaxioService maxio) => _maxio = maxio;

    [Authorize]
    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(Summary = "List subscription plans", Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<List<SubscriptionPlanResponse>>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var plans = await _maxio.GetPlansAsync(cancellationToken);
        return plans.Select(p => new SubscriptionPlanResponse(p.Id, p.Handle, p.Name, p.PriceInCents, p.IntervalUnit)).ToList();
    }
}

public record SubscriptionPlanResponse(int Id, string Handle, string Name, int PriceInCents, string IntervalUnit);
