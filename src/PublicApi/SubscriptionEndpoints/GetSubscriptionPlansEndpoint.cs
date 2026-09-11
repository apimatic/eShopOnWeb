using System.Collections.Generic;
using System.Net;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetSubscriptionPlansEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<List<SubscriptionPlanResponse>>
{
    private readonly ISubscriptionService _service;
    public GetSubscriptionPlansEndpoint(ISubscriptionService service) => _service = service;

    [Authorize]
    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(Summary = "List subscription plans", Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<List<SubscriptionPlanResponse>>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var plans = await _service.GetPlansAsync();
        return Ok(plans.ConvertAll(p => new SubscriptionPlanResponse(p.Handle, p.Name, p.Price, p.IntervalUnit)));
    }
}

public record SubscriptionPlanResponse(string Handle, string Name, decimal Price, string IntervalUnit);
