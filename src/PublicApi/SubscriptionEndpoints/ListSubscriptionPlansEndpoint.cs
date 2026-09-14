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
/// Lists the plans a shopper can subscribe to (the products published in the configured Maxio product family).
/// </summary>
public class ListSubscriptionPlansEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<ListSubscriptionPlansResponse>
{
    private readonly IMaxioBillingService _subscriptionService;

    public ListSubscriptionPlansEndpoint(IMaxioBillingService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(
        Summary = "Lists available subscription plans",
        Description = "Lists the subscription plans a shopper can subscribe to",
        OperationId = "subscriptions.plans.list",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<ListSubscriptionPlansResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var plans = await _subscriptionService.ListAvailablePlansAsync(cancellationToken);
        var site = await _subscriptionService.GetSiteAsync(cancellationToken);
        var currency = site.Currency ?? "USD";

        var response = new ListSubscriptionPlansResponse();
        response.Plans.AddRange(plans.Select(plan => SubscriptionDtoMapper.ToPlanDto(plan, currency)));
        return response;
    }
}
