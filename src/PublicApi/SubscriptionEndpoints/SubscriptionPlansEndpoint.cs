using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlansEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<SubscriptionPlansResponse>
{
    private readonly IMaxioBillingService _maxio;

    public SubscriptionPlansEndpoint(IMaxioBillingService maxio)
    {
        _maxio = maxio;
    }

    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(Summary = "List subscription plans", OperationId = "subscriptions.plans", Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<SubscriptionPlansResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var response = new SubscriptionPlansResponse(Guid.NewGuid());
        var plans = await _maxio.ListPlansAsync();
        foreach (var p in plans)
        {
            response.Plans.Add(new PlanDto
            {
                Id = p.Id,
                Handle = p.Handle,
                Name = p.Name,
                Price = p.PriceInCents,
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit
            });
        }
        return Ok(response);
    }
}
