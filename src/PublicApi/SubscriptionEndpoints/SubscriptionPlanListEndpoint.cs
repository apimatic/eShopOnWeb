using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans available to shoppers.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class SubscriptionPlanListEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<SubscriptionPlanListResponse>
{
    private readonly IMaxioSubscriptionService _subscriptionService;

    public SubscriptionPlanListEndpoint(IMaxioSubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(
        Summary = "Lists the available subscription plans",
        Description = "Lists the subscription plans available in the configured Maxio product family",
        OperationId = "subscriptions.listPlans",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<SubscriptionPlanListResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var response = new SubscriptionPlanListResponse(Guid.NewGuid());

        var plans = await _subscriptionService.ListPlansAsync(cancellationToken);
        response.Plans.AddRange(plans.Select(p => new SubscriptionPlanDto
        {
            Handle = p.Handle,
            Name = p.Name,
            Description = p.Description,
            Price = p.Price,
            IntervalUnit = p.IntervalUnit,
            Interval = p.Interval
        }));

        return Ok(response);
    }
}
