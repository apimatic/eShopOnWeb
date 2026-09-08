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
/// Lists the Maxio subscriptions of the signed-in shopper.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class MySubscriptionsEndpoint : EndpointBaseAsync.WithoutRequest.WithActionResult<MySubscriptionsResponse>
{
    private readonly ISubscriptionService _subscriptionService;

    public MySubscriptionsEndpoint(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "Lists the signed-in shopper's subscriptions",
        Description = "Returns the Maxio subscriptions belonging to the shopper identified by the bearer token",
        OperationId = "subscriptions.my",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<MySubscriptionsResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var userName = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Unauthorized();
        }

        var response = new MySubscriptionsResponse();
        var subscriptions = await _subscriptionService.ListMySubscriptionsAsync(userName, cancellationToken);
        foreach (var subscription in subscriptions)
        {
            response.Subscriptions.Add(SubscriptionDtos.From(subscription));
        }

        return Ok(response);
    }
}
