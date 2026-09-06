using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetSubscriptionPlansEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<GetSubscriptionPlansListResponse>
{
    private readonly IMaxioApiClient _maxioClient;

    public GetSubscriptionPlansEndpoint(IMaxioApiClient maxioClient)
    {
        _maxioClient = maxioClient;
    }

    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(
        Summary = "List subscription plans",
        Description = "Get available subscription plans",
        OperationId = "subscriptions.list_plans",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    [AllowAnonymous]
    public override async Task<ActionResult<GetSubscriptionPlansListResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var response = new GetSubscriptionPlansListResponse();

        var plans = await _maxioClient.GetPlansAsync();
        foreach (var plan in plans)
        {
            response.Plans.Add(new SubscriptionPlanDto
            {
                Id = plan.Id,
                Name = plan.Name,
                Handle = plan.Handle,
                Price = plan.PriceInCents / 100m,
                BillingIntervalDays = plan.Interval,
                BillingIntervalUnit = plan.IntervalUnit,
                Description = plan.Description
            });
        }

        return Ok(response);
    }
}
