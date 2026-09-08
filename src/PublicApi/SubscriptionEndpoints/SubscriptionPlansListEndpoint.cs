using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Lists the plans a shopper can subscribe to.</summary>
public class SubscriptionPlansListEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<SubscriptionPlansListResponse>
{
    private readonly SubscriptionService _subscriptionService;

    public SubscriptionPlansListEndpoint(SubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    [HttpGet("api/subscription-plans")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [SwaggerOperation(
        Summary = "Lists the available subscription plans",
        Description = "Lists the plans (products) available for subscription from the configured Maxio product family.",
        OperationId = "subscriptions.listPlans",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<SubscriptionPlansListResponse>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        var plans = await _subscriptionService.ListPlansAsync(cancellationToken);

        var response = new SubscriptionPlansListResponse();
        response.Plans.AddRange(plans);
        return Ok(response);
    }
}
