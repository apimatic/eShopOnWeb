using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionPlanEndpoints;

public class SubscriptionPlanListEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<ListSubscriptionPlansResponse>
{
    private readonly ISubscriptionBillingService _billing;

    public SubscriptionPlanListEndpoint(ISubscriptionBillingService billing)
    {
        _billing = billing;
    }

    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(
        Summary = "Lists the subscription plans available from Maxio",
        OperationId = "subscriptions.listPlans",
        Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<ListSubscriptionPlansResponse>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        var plans = await _billing.GetPlansAsync(cancellationToken);
        return Ok(new ListSubscriptionPlansResponse
        {
            Plans = plans.Select(plan => new SubscriptionPlanDto
            {
                Id = plan.Id,
                Name = plan.Name,
                ProductHandle = plan.Handle,
                Description = plan.Description,
                PriceInCents = plan.PriceInCents,
                Interval = plan.Interval,
                IntervalUnit = plan.IntervalUnit,
                RequiresPaymentMethod = plan.RequiresPaymentMethod
            }).ToList()
        });
    }
}
