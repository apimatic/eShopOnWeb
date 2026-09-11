using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionPlanEndpoints;

public class SubscriptionPlanListEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<ListSubscriptionPlansResponse>
{
    private readonly IMaxioSubscriptionPlanService _planService;

    public SubscriptionPlanListEndpoint(IMaxioSubscriptionPlanService planService)
    {
        _planService = planService;
    }

    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(
        Summary = "Lists available subscription plans",
        Description = "Returns all available subscription plans from the billing system",
        OperationId = "subscriptionPlans.list",
        Tags = new[] { "SubscriptionPlanEndpoints" })
    ]
    public override async Task<ActionResult<ListSubscriptionPlansResponse>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        var response = new ListSubscriptionPlansResponse(Guid.NewGuid());

        var plans = await _planService.GetPlansAsync();
        response.Plans.AddRange(plans);

        return Ok(response);
    }
}

public interface IMaxioSubscriptionPlanService
{
    Task<List<PlanDto>> GetPlansAsync();
}

public class ListSubscriptionPlansResponse : BaseResponse
{
    public ListSubscriptionPlansResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListSubscriptionPlansResponse()
    {
    }

    public List<PlanDto> Plans { get; set; } = new();
}
