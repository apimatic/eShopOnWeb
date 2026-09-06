using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi.Services;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetSubscriptionPlansEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<GetSubscriptionPlansResponse>
{
    private readonly IMaxioService _maxioService;

    public GetSubscriptionPlansEndpoint(IMaxioService maxioService)
    {
        _maxioService = maxioService;
    }

    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(
        Summary = "Get available subscription plans",
        Description = "Get available subscription plans",
        OperationId = "subscriptions.plans",
        Tags = new[] { "SubscriptionEndpoints" }
    )]
    public override async Task<ActionResult<GetSubscriptionPlansResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var response = await _maxioService.GetSubscriptionPlansAsync(cancellationToken);
        return Ok(response);
    }
}

public class GetSubscriptionPlansResponse
{
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}

public class SubscriptionPlanDto
{
    public long Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Description { get; set; } = string.Empty;
    public int IntervalValue { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
}
