using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi.SubscriptionServices;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsEndpoint : EndpointBaseAsync.WithoutRequest.WithActionResult<MySubscriptionsResponse>
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly ICurrentShopperService _currentShopperService;

    public MySubscriptionsEndpoint(ISubscriptionService subscriptionService, ICurrentShopperService currentShopperService)
    {
        _subscriptionService = subscriptionService;
        _currentShopperService = currentShopperService;
    }

    [HttpGet("api/my-subscriptions")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [SwaggerOperation(
        Summary = "Lists the current user's subscriptions",
        Description = "Lists the subscriptions held by the current user",
        OperationId = "subscriptions.my",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<MySubscriptionsResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var shopper = await _currentShopperService.GetCurrentShopperAsync(User, cancellationToken);
        if (shopper == null)
        {
            return Unauthorized();
        }

        var subscriptions = await _subscriptionService.GetSubscriptionsAsync(shopper, cancellationToken);
        var response = new MySubscriptionsResponse { Subscriptions = subscriptions.ToList() };
        return response;
    }
}
