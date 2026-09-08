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
/// Lists the subscription plans available in the configured Maxio product family.
/// </summary>
public class ListSubscriptionPlansEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<ListSubscriptionPlansResponse>
{
    private readonly MaxioSubscriptionService _maxio;

    public ListSubscriptionPlansEndpoint(MaxioSubscriptionService maxio)
    {
        _maxio = maxio;
    }

    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(
        Summary = "Lists subscription plans",
        Description = "Lists the subscription plans available in the configured Maxio product family",
        OperationId = "subscriptions.plans",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<ListSubscriptionPlansResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var plans = await _maxio.ListPlansAsync(cancellationToken);
        var response = new ListSubscriptionPlansResponse
        {
            Plans = plans.ToList()
        };
        return Ok(response);
    }
}
