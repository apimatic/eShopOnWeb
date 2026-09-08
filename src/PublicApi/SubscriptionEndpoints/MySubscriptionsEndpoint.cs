using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Lists the subscriptions of the authenticated shopper.</summary>
public class MySubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<MySubscriptionsResponse>
{
    private readonly SubscriptionService _subscriptionService;

    public MySubscriptionsEndpoint(SubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    [HttpGet("api/my-subscriptions")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [SwaggerOperation(
        Summary = "Lists the current user's subscriptions",
        Description = "Returns the subscriptions owned by the authenticated user in Maxio, the billing " +
                      "system of record, including plan, price, state and next billing date.",
        OperationId = "subscriptions.mine",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<MySubscriptionsResponse>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        string? userReference = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userReference))
        {
            return Unauthorized();
        }

        var subscriptions = await _subscriptionService.ListMySubscriptionsAsync(userReference, cancellationToken);

        var response = new MySubscriptionsResponse();
        response.Subscriptions.AddRange(subscriptions);
        return Ok(response);
    }
}
