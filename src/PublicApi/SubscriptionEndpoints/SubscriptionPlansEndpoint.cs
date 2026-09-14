using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi.SubscriptionServices;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlansEndpoint : EndpointBaseAsync.WithoutRequest.WithActionResult<ListSubscriptionPlansResponse>
{
    private readonly ISubscriptionService _subscriptionService;

    public SubscriptionPlansEndpoint(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    [HttpGet("api/subscription-plans")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [SwaggerOperation(
        Summary = "Lists the subscription plans available to the current user",
        Description = "Lists the subscription plans available to the current user",
        OperationId = "subscriptions.listPlans",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<ListSubscriptionPlansResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var plans = await _subscriptionService.GetPlansAsync(cancellationToken);
        var response = new ListSubscriptionPlansResponse { Plans = plans.ToList() };
        return response;
    }
}
