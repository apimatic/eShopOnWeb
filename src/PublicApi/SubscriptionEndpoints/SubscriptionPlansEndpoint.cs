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
public class SubscriptionPlansEndpoint : EndpointBaseAsync
    .WithRequest<SubscriptionPlansRequest>
    .WithActionResult<SubscriptionPlansResponse>
{
    private readonly IMaxioService _maxio;

    public SubscriptionPlansEndpoint(IMaxioService maxio)
    {
        _maxio = maxio;
    }

    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(
        Summary = "List subscription plans",
        Description = "Returns available Maxio subscription plans from the configured product family.",
        OperationId = "subscription.plans",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<SubscriptionPlansResponse>> HandleAsync(
        [FromQuery] SubscriptionPlansRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = new SubscriptionPlansResponse(request.CorrelationId());
        var plans = await _maxio.GetSubscriptionPlansAsync();
        response.Plans = plans.Select(p => new PlanResponse
        {
            Id = p.Id,
            Handle = p.Handle,
            Name = p.Name,
            Price = p.Price,
            Currency = p.Currency
        }).ToList();
        return response;
    }
}
