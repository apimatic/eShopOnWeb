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

public sealed class SubscriptionPlanListEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<SubscriptionPlanListResponse>
{
    private readonly ISubscriptionBillingService _billingService;

    public SubscriptionPlanListEndpoint(ISubscriptionBillingService billingService)
    {
        _billingService = billingService;
    }

    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(Summary = "Lists available recurring subscription plans.",
        OperationId = "subscriptions.listPlans", Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<SubscriptionPlanListResponse>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        var plans = await _billingService.ListPlansAsync(cancellationToken);
        return Ok(new SubscriptionPlanListResponse { Plans = plans.Select(plan => plan.ToDto()).ToArray() });
    }
}
