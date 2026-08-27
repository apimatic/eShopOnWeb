using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscriptionPlanListEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<IReadOnlyList<SubscriptionPlanDto>>
{
    private readonly ISubscriptionBillingService _billingService;

    public SubscriptionPlanListEndpoint(ISubscriptionBillingService billingService)
    {
        _billingService = billingService;
    }

    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(
        Summary = "Lists available subscription plans",
        OperationId = "subscriptions.listPlans",
        Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<IReadOnlyList<SubscriptionPlanDto>>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        return Ok(await _billingService.ListPlansAsync(cancellationToken));
    }
}
