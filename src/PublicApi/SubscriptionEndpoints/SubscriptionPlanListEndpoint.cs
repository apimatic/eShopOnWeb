using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class SubscriptionPlanListEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<List<SubscriptionPlanDto>>
{
    private readonly IMaxioService _maxioService;

    public SubscriptionPlanListEndpoint(IMaxioService maxioService)
    {
        _maxioService = maxioService;
    }

    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(
        Summary = "Lists available subscription plans",
        Description = "Lists available subscription plans from Maxio Advanced Billing",
        OperationId = "subscription-plans.list",
        Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<List<SubscriptionPlanDto>>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        var plans = await _maxioService.ListPlansAsync(cancellationToken);
        return Ok(plans);
    }
}
