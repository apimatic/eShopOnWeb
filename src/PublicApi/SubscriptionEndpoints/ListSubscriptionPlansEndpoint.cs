using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists available subscription plans
/// </summary>
[Authorize]
public class ListSubscriptionPlansEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<ListSubscriptionPlansResponse>
{
    private readonly ISubscriptionService _subscriptionService;

    public ListSubscriptionPlansEndpoint(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(
        Summary = "Lists available subscription plans",
        Description = "Returns a list of available subscription plans from Maxio",
        OperationId = "subscription.listPlans",
        Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<ListSubscriptionPlansResponse>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        var response = new ListSubscriptionPlansResponse(System.Guid.NewGuid());

        try
        {
            var plans = await _subscriptionService.ListPlansAsync(cancellationToken);
            response.Plans = plans;
            return Ok(response);
        }
        catch (Exception ex)
        {
            response.ErrorMessage = ex.Message;
            return StatusCode(500, response);
        }
    }
}

public class ListSubscriptionPlansResponse : BaseResponse
{
    public ListSubscriptionPlansResponse(System.Guid correlationId) : base(correlationId)
    {
    }

    public IReadOnlyList<SubscriptionPlanDto> Plans { get; set; } = new List<SubscriptionPlanDto>();
    public string? ErrorMessage { get; set; }
}
