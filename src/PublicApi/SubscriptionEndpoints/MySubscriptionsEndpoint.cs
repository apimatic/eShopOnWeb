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
/// Lists the signed-in shopper's subscriptions (from Maxio, the billing system of record).
/// </summary>
public class MySubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<MySubscriptionsResponse>
{
    private readonly IMaxioBillingService _subscriptionService;

    public MySubscriptionsEndpoint(IMaxioBillingService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "Lists the current shopper's subscriptions",
        Description = "Lists the subscriptions belonging to the signed-in shopper",
        OperationId = "subscriptions.mine.list",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<MySubscriptionsResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var shopperReference = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(shopperReference))
        {
            return Unauthorized();
        }

        var subscriptions = await _subscriptionService.ListSubscriptionsAsync(shopperReference, cancellationToken);
        var site = await _subscriptionService.GetSiteAsync(cancellationToken);
        var currency = site.Currency ?? "USD";

        var response = new MySubscriptionsResponse();
        response.Subscriptions.AddRange(
            subscriptions
                .OrderByDescending(subscription => subscription.CreatedAt)
                .ThenByDescending(subscription => subscription.Id)
                .Select(subscription => SubscriptionDtoMapper.ToSubscriptionDto(subscription, currency)));

        return response;
    }
}
