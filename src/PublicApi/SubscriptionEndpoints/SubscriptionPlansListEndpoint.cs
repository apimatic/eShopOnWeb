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
/// Lists the subscription plans available to shoppers.
/// </summary>
public class SubscriptionPlansListEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<SubscriptionPlansListResponse>
{
    private readonly ISubscriptionService _subscriptionService;

    public SubscriptionPlansListEndpoint(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    [HttpGet("api/subscription-plans")]
    [Authorize]
    [SwaggerOperation(
        Summary = "Lists the subscription plans available to shoppers",
        Description = "Lists the subscription plans available to shoppers",
        OperationId = "subscriptions.plans",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<SubscriptionPlansListResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var response = new SubscriptionPlansListResponse();

        var plans = await _subscriptionService.ListPlansAsync(cancellationToken);

        response.Plans.AddRange(plans.Select(plan => new SubscriptionPlanDto
        {
            Id = plan.Id,
            Handle = plan.Handle,
            Name = plan.Name,
            Description = plan.Description,
            PriceInCents = plan.PriceInCents,
            Interval = plan.Interval,
            IntervalUnit = plan.IntervalUnit,
            ProductFamilyHandle = plan.ProductFamilyHandle,
            Components = plan.Components.Select(component => new SubscriptionComponentDto
            {
                Id = component.Id,
                Handle = component.Handle,
                Name = component.Name,
                Kind = component.Kind,
                UnitName = component.UnitName,
                UnitPrice = component.UnitPrice,
                PricePerUnitInCents = component.PricePerUnitInCents
            }).ToList()
        }));

        return Ok(response);
    }
}
