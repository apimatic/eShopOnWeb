using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[Authorize]
public class SubscriptionPlansListEndpoint : EndpointBaseAsync.WithoutRequest.WithActionResult<SubscriptionPlansListResponse>
{
    private readonly IMaxioBillingService _billingService;
    private readonly MaxioSettings _maxioSettings;

    public SubscriptionPlansListEndpoint(IMaxioBillingService billingService, MaxioSettings maxioSettings)
    {
        _billingService = billingService;
        _maxioSettings = maxioSettings;
    }

    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(
        Summary = "List subscription plans",
        Description = "List available subscription plans",
        OperationId = "subscriptions.list-plans",
        Tags = new[] { "SubscriptionEndpoints" }
    )]
    [ProducesResponseType(typeof(SubscriptionPlansListResponse), 200)]
    [ProducesResponseType(500)]
    public override async Task<ActionResult<SubscriptionPlansListResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var response = new SubscriptionPlansListResponse();

        try
        {
            var plans = await _billingService.GetPlansAsync(_maxioSettings.ProductFamilyHandle);

            response.Plans = plans.Select(p => new SubscriptionPlanResponse
            {
                Id = p.Id,
                Handle = p.Handle,
                Name = p.Name,
                Description = p.Description,
                Price = p.PriceInCents / 100m,
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit
            }).ToList();

            response.Success = true;
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Failed to retrieve subscription plans", message = ex.Message });
        }

        return Ok(response);
    }
}

public class SubscriptionPlansListResponse
{
    public List<SubscriptionPlanResponse> Plans { get; set; } = new();
    public bool Success { get; set; }
}

public class SubscriptionPlanResponse
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
}
