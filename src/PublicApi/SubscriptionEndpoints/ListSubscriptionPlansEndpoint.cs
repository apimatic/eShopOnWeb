using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi.SubscriptionBilling;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[Controller]
[Authorize]
public class ListSubscriptionPlansEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<List<SubscriptionPlan>>
{
    private readonly ISubscriptionBillingService _billingService;

    public ListSubscriptionPlansEndpoint(ISubscriptionBillingService billingService)
    {
        _billingService = billingService;
    }

    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(
        Summary = "Lists available recurring-subscription plans",
        OperationId = "subscriptions.listPlans",
        Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<List<SubscriptionPlan>>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var plans = await _billingService.ListPlansAsync(cancellationToken);
        return Ok(plans);
    }
}
