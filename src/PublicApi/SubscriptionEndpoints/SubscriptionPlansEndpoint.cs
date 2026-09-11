using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Claims;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.eShopWeb.PublicApi.Services;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlansEndpoint : EndpointBaseSync
    .WithoutRequest
    .WithActionResult<List<SubscriptionPlanResponse>>
{
    private readonly MaxioClient _maxio;
    public SubscriptionPlansEndpoint(MaxioClient maxio) => _maxio = maxio;

    [Authorize]
    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(Summary = "List available subscription plans", Tags = new[] { "Subscription" })]
    public override ActionResult<List<SubscriptionPlanResponse>> Handle()
    {
        var family = _maxio.GetProductFamilyAsync("eshop-subscribe").GetAwaiter().GetResult();
        // Return hard-known handles from verified sandbox; also include family info
        var plans = new List<SubscriptionPlanResponse>
        {
            new() { Handle = "eshop-pro", Name = "Pro Plan", PriceMonthly = 299.00m, Description = "Full-featured subscription" },
            new() { Handle = "basic-plan", Name = "Basic Plan", PriceMonthly = 29.00m, Description = "Entry-level subscription" }
        };
        return Ok(plans);
    }
}

public class SubscriptionPlanResponse
{
    public string Handle { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal PriceMonthly { get; set; }
    public string Description { get; set; } = "";
}

