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
/// Lists the subscription plans shoppers can subscribe to.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class SubscriptionPlanListEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<SubscriptionPlanListResponse>
{
    private readonly ISubscriptionService _subscriptionService;

    public SubscriptionPlanListEndpoint(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(
        Summary = "List subscription plans",
        Description = "Lists the subscription plans available to shoppers",
        OperationId = "subscriptions.listPlans",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<SubscriptionPlanListResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var plans = await _subscriptionService.ListPlansAsync(cancellationToken);

        var response = new SubscriptionPlanListResponse
        {
            SubscriptionPlans = plans.Select(p => new SubscriptionPlanDto
            {
                Id = p.Id,
                Handle = p.Handle,
                Name = p.Name,
                Description = p.Description,
                Price = p.Price,
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit,
                RequireCreditCard = p.RequireCreditCard
            }).ToList()
        };

        return Ok(response);
    }
}
