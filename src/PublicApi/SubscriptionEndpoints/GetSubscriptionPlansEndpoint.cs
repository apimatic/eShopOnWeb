using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi.Services;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[Authorize]
public class GetSubscriptionPlansEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<GetSubscriptionPlansResponse>
{
    private readonly MaxioSubscriptionService _subscriptionService;

    public GetSubscriptionPlansEndpoint(MaxioSubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(
        Summary = "Get available subscription plans",
        Description = "Returns a list of available subscription plans",
        OperationId = "subscriptions.getPlans",
        Tags = new[] { "Subscriptions" })
    ]
    [AllowAnonymous]
    public override async Task<ActionResult<GetSubscriptionPlansResponse>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var plans = await _subscriptionService.GetSubscriptionPlansAsync(cancellationToken);
            return Ok(new GetSubscriptionPlansResponse
            {
                Plans = plans,
                Result = true
            });
        }
        catch (MaxioServiceException ex)
        {
            return StatusCode(500, new GetSubscriptionPlansResponse
            {
                Result = false,
                Error = ex.Message
            });
        }
    }
}

public class GetSubscriptionPlansResponse : BaseResponse
{
    public bool Result { get; set; } = true;
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
    public string? Error { get; set; }
}
