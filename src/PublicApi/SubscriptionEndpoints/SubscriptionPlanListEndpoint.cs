using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans (Maxio products) that the current user can subscribe to.
/// </summary>
public class SubscriptionPlanListEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<ListSubscriptionPlansResponse>
{
    private readonly MaxioSubscriptionService _subscriptionService;

    public SubscriptionPlanListEndpoint(MaxioSubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    [HttpGet("api/subscription-plans")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [SwaggerOperation(
        Summary = "Lists available subscription plans",
        Description = "Lists the plans in the configured Maxio product family that can be subscribed to without a payment method.",
        OperationId = "subscriptions.listPlans",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<ListSubscriptionPlansResponse>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        var response = new ListSubscriptionPlansResponse();

        var plans = await _subscriptionService.ListAvailablePlansAsync(cancellationToken);
        response.Plans.AddRange(plans.Select(plan => plan.ToPlanDto()));

        return Ok(response);
    }
}
