using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[Authorize]
public class SubscriptionPlansEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<SubscriptionPlansResponse>
{
    private readonly SubscriptionService _subscriptionService;

    public SubscriptionPlansEndpoint(SubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(
        Summary = "List subscription plans",
        Description = "Retrieve available subscription plans",
        OperationId = "subscriptions.listPlans",
        Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<SubscriptionPlansResponse>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        var response = new SubscriptionPlansResponse();

        try
        {
            var plans = await _subscriptionService.GetSubscriptionPlansAsync(cancellationToken);
            response.Plans.AddRange(plans);
            return Ok(response);
        }
        catch (SubscriptionException ex)
        {
            return StatusCode(ex.HttpStatusCode ?? 500, new { error = ex.Message });
        }
    }
}

public class SubscriptionPlansResponse : BaseResponse
{
    public List<SubscriptionPlanDto> Plans { get; } = new();
}
