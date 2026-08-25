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
/// Lists the authenticated caller's subscriptions
/// </summary>
public class ListMySubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<ListMySubscriptionsResponse>
{
    private readonly IMaxioSubscriptionService _subscriptionService;

    public ListMySubscriptionsEndpoint(IMaxioSubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    [HttpGet("api/my-subscriptions")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [SwaggerOperation(
        Summary = "Lists the caller's subscriptions",
        Description = "Lists the authenticated caller's Maxio subscriptions with state, plan, price and next billing date",
        OperationId = "subscriptions.listMine",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<ListMySubscriptionsResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var username = User.Identity?.Name;
        if (string.IsNullOrEmpty(username))
        {
            return Unauthorized();
        }

        var response = new ListMySubscriptionsResponse();
        response.Subscriptions = await _subscriptionService.ListSubscriptionsAsync(username, cancellationToken);
        return Ok(response);
    }
}
