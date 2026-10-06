using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans available for enrollment
/// </summary>
[Authorize]
public class ListSubscriptionPlansEndpoint : EndpointBaseAsync
    .WithRequest<ListSubscriptionPlansRequest>
    .WithActionResult<ListSubscriptionPlansResponse>
{
    private readonly ISubscriptionService _subscriptionService;

    public ListSubscriptionPlansEndpoint(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(
        Summary = "Lists the subscription plans available for enrollment",
        Description = "Lists the subscription plans available for enrollment",
        OperationId = "subscriptions.listPlans",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<ListSubscriptionPlansResponse>> HandleAsync([FromQuery] ListSubscriptionPlansRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = new ListSubscriptionPlansResponse(request.CorrelationId());

        var plans = await _subscriptionService.GetPlansAsync(cancellationToken);

        response.Plans.AddRange(plans.Select(p => new SubscriptionPlanDto
        {
            Handle = p.Handle,
            Name = p.Name,
            Description = p.Description,
            Price = p.Price,
            Interval = p.Interval,
            IntervalUnit = p.IntervalUnit,
            Taxable = p.Taxable
        }));

        return response;
    }
}