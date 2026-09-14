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
/// Lists the subscription plans (products) available in the configured Maxio product family.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class ListSubscriptionPlansEndpoint : EndpointBaseAsync
    .WithRequest<ListSubscriptionPlansRequest>
    .WithActionResult<ListSubscriptionPlansResponse>
{
    private readonly IMaxioBillingService _maxioBillingService;

    public ListSubscriptionPlansEndpoint(IMaxioBillingService maxioBillingService)
    {
        _maxioBillingService = maxioBillingService;
    }

    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(
        Summary = "List subscription plans",
        Description = "Lists the recurring subscription plans a shopper can subscribe to.",
        OperationId = "subscriptions.list-plans",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<ListSubscriptionPlansResponse>> HandleAsync(
        [FromQuery] ListSubscriptionPlansRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = new ListSubscriptionPlansResponse(request.CorrelationId());

        var plans = await _maxioBillingService.GetAvailablePlansAsync(cancellationToken);
        response.Plans.AddRange(plans.Select(SubscriptionMapping.ToPlanDto));

        return Ok(response);
    }
}
