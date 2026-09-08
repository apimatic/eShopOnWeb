using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the plans a shopper can subscribe to (from the Maxio product family configured for the
/// storefront).
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class ListSubscriptionPlansEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<SubscriptionPlansListResponse>
{
    private readonly ISubscriptionBillingService _subscriptionBillingService;

    public ListSubscriptionPlansEndpoint(ISubscriptionBillingService subscriptionBillingService)
    {
        _subscriptionBillingService = subscriptionBillingService;
    }

    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(
        Summary = "Lists available subscription plans",
        Description = "Lists the plans available to subscribe to. The catalog lives in Maxio Advanced Billing.",
        OperationId = "subscriptions.listPlans",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<SubscriptionPlansListResponse>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        var response = new SubscriptionPlansListResponse();

        var plans = await _subscriptionBillingService.ListAvailablePlansAsync(cancellationToken);
        foreach (var plan in plans)
        {
            response.Plans.Add(SubscriptionPlanDto.FromMaxio(plan));
        }

        return Ok(response);
    }
}
