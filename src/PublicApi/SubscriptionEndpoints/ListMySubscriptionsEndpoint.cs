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
/// Lists the subscriptions belonging to the current user.
/// </summary>
public class ListMySubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<ListMySubscriptionsResponse>
{
    private readonly ISubscriptionService _subscriptionService;

    public ListMySubscriptionsEndpoint(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    [HttpGet("api/my-subscriptions")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [SwaggerOperation(
        Summary = "Lists the subscriptions belonging to the current user",
        Description = "Lists the subscriptions belonging to the current user",
        OperationId = "subscriptions.mine",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<ListMySubscriptionsResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var userEmail = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userEmail))
        {
            return Unauthorized();
        }

        var subscriptions = await _subscriptionService.GetMySubscriptionsAsync(userEmail, cancellationToken);

        var response = new ListMySubscriptionsResponse();
        foreach (var subscription in subscriptions)
        {
            response.Subscriptions.Add(SubscriptionDto.FromSubscription(subscription));
        }

        return Ok(response);
    }
}
