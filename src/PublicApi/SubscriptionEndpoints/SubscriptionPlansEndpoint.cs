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

public class SubscriptionPlansEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<SubscriptionPlansResponse>
{
    private readonly IMaxioService _maxioService;

    public SubscriptionPlansEndpoint(IMaxioService maxioService)
    {
        _maxioService = maxioService;
    }

    [HttpGet("api/subscription-plans")]
    [AllowAnonymous]
    [SwaggerOperation(
        Summary = "List subscription plans",
        Description = "Returns available subscription plans from Maxio",
        OperationId = "subscriptions.list-plans",
        Tags = new[] { "SubscriptionEndpoints" }
    )]
    public override async Task<ActionResult<SubscriptionPlansResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var response = new SubscriptionPlansResponse();

        try
        {
            var plans = await _maxioService.GetSubscriptionPlansAsync();
            response.Plans.AddRange(plans);
            response.Success = true;
        }
        catch (Exception ex)
        {
            response.Success = false;
            response.Error = ex.Message;
            return BadRequest(response);
        }

        return Ok(response);
    }
}

public class SubscriptionPlansResponse : BaseResponse
{
    public SubscriptionPlansResponse() { }
    public SubscriptionPlansResponse(Guid correlationId) : base(correlationId) { }

    public bool Success { get; set; }
    public string Error { get; set; } = string.Empty;
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}
