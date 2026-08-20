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
/// Lists Maxio subscription plans in the configured product family.
/// </summary>
public class ListSubscriptionPlansEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<ListSubscriptionPlansResponse>
{
    private readonly ISubscriptionBillingService _billing;

    public ListSubscriptionPlansEndpoint(ISubscriptionBillingService billing)
    {
        _billing = billing;
    }

    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(
        Summary = "List subscription plans",
        Description = "Returns the Maxio Advanced Billing products in the configured product family.",
        OperationId = "subscriptions.listPlans",
        Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<ListSubscriptionPlansResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var plans = await _billing.ListPlansAsync(cancellationToken);
        var response = new ListSubscriptionPlansResponse
        {
            Plans = plans.Select(SubscriptionPlanDto.From).ToList()
        };
        return Ok(response);
    }
}
