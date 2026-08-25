using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi.Services;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans available in the configured Maxio product family
/// </summary>
public class ListSubscriptionPlansEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<ListSubscriptionPlansResponse>
{
    private readonly IMaxioSubscriptionService _subscriptionService;

    public ListSubscriptionPlansEndpoint(IMaxioSubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    [HttpGet("api/subscription-plans")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [SwaggerOperation(
        Summary = "Lists available subscription plans",
        Description = "Lists the subscription plans available in the configured Maxio product family",
        OperationId = "subscriptions.listPlans",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<ListSubscriptionPlansResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var response = new ListSubscriptionPlansResponse();
        response.Plans = await _subscriptionService.ListPlansAsync(cancellationToken);
        return Ok(response);
    }
}
