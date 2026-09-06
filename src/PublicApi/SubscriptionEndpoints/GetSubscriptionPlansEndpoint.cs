using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.Infrastructure.Services;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetSubscriptionPlansEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<GetSubscriptionPlansResponse>
{
    private readonly IMaxioBillingService _billingService;

    public GetSubscriptionPlansEndpoint(IMaxioBillingService billingService)
    {
        _billingService = billingService;
    }

    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(
        Summary = "Get available subscription plans",
        Description = "Returns a list of available subscription plans from Maxio",
        OperationId = "subscriptions.getPlans",
        Tags = new[] { "SubscriptionEndpoints" }
    )]
    public override async Task<ActionResult<GetSubscriptionPlansResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var plans = await _billingService.GetSubscriptionPlansAsync();
            var response = new GetSubscriptionPlansResponse()
            {
                Plans = plans.Select(p => new SubscriptionPlanDto
                {
                    Id = p.Id,
                    Name = p.Name,
                    Handle = p.Handle,
                    Description = p.Description,
                    Price = p.GetPrice(),
                    Interval = p.Interval,
                    IntervalUnit = p.IntervalUnit
                }).ToList()
            };
            return Ok(response);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}

public class GetSubscriptionPlansResponse
{
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}
