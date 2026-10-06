using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Maxio.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans available for purchase.
/// </summary>
public class ListSubscriptionPlansEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<ListSubscriptionPlansResponse>
{
    private readonly ISubscriptionService _subscriptionService;

    public ListSubscriptionPlansEndpoint(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    [HttpGet("api/subscription-plans")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [SwaggerOperation(
        Summary = "Lists the subscription plans available for purchase",
        Description = "Lists the subscription plans available for purchase",
        OperationId = "subscriptions.plans",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<ListSubscriptionPlansResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var plans = await _subscriptionService.GetPlansAsync(cancellationToken);

        var response = new ListSubscriptionPlansResponse();
        foreach (var plan in plans)
        {
            response.Plans.Add(SubscriptionPlanDto.FromProduct(plan));
        }

        return Ok(response);
    }
}
