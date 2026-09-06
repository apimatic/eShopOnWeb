using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated shopper's own subscriptions.
/// </summary>
public class MySubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<ListMySubscriptionsResponse>
{
    private readonly ISubscriptionBillingService _subscriptionBillingService;
    private readonly ISubscriberIdentityAccessor _subscriberIdentityAccessor;

    public MySubscriptionsEndpoint(ISubscriptionBillingService subscriptionBillingService,
        ISubscriberIdentityAccessor subscriberIdentityAccessor)
    {
        _subscriptionBillingService = subscriptionBillingService;
        _subscriberIdentityAccessor = subscriberIdentityAccessor;
    }

    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "Lists the authenticated shopper's subscriptions",
        Description = "Reads the caller's subscriptions straight from the billing system of record, " +
                      "so the result is correct across application restarts.",
        OperationId = "subscriptions.listMine",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    [ProducesResponseType(typeof(ListMySubscriptionsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public override async Task<ActionResult<ListMySubscriptionsResponse>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        var subscriber = await _subscriberIdentityAccessor.ResolveAsync(User);

        if (subscriber is null)
        {
            return Unauthorized();
        }

        var subscriptions = await _subscriptionBillingService.ListSubscriptionsAsync(subscriber, cancellationToken);

        var response = new ListMySubscriptionsResponse
        {
            Subscriptions = subscriptions.Select(SubscriptionDto.FromSubscription).ToList()
        };
        response.ActiveCount = response.Subscriptions.Count(subscription => subscription.IsActive);

        return Ok(response);
    }
}
