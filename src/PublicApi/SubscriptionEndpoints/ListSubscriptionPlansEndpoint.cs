using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[Authorize]
public class ListSubscriptionPlansEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<ListSubscriptionPlansResponse>
{
    private readonly IMaxioService _maxioService;
    private readonly MaxioSettings _maxioSettings;

    public ListSubscriptionPlansEndpoint(IMaxioService maxioService, MaxioSettings maxioSettings)
    {
        _maxioService = maxioService;
        _maxioSettings = maxioSettings;
    }

    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(
        Summary = "Get available subscription plans",
        Description = "Returns a list of available subscription plans",
        OperationId = "subscriptions.listPlans",
        Tags = new[] { "Subscriptions" }
    )]
    public override async Task<ActionResult<ListSubscriptionPlansResponse>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrEmpty(_maxioSettings.ProductFamilyHandle))
            {
                return BadRequest("Subscription service is not configured");
            }

            var products = await _maxioService.GetProductsByFamilyHandle(_maxioSettings.ProductFamilyHandle);

            var plans = products.Select(p => new SubscriptionPlanDto
            {
                Id = p.Id,
                Name = p.Name,
                Handle = p.Handle,
                Description = p.Description,
                Price = p.PriceInCents / 100m,
                BillingCycle = $"{p.Interval} {p.IntervalUnit}"
            }).ToList();

            return Ok(new ListSubscriptionPlansResponse { Plans = plans });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }
}

public class ListSubscriptionPlansResponse
{
    public List<SubscriptionPlanDto>? Plans { get; set; }
}
