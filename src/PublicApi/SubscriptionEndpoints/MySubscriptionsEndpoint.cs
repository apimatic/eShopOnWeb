using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Returns the authenticated shopper's own subscriptions, read live from the billing system.
/// </summary>
public class MySubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<MySubscriptionsResponse>
{
    private readonly ISubscriptionBillingService _subscriptionBillingService;
    private readonly ISubscriberResolver _subscriberResolver;

    public MySubscriptionsEndpoint(
        ISubscriptionBillingService subscriptionBillingService,
        ISubscriberResolver subscriberResolver)
    {
        _subscriptionBillingService = subscriptionBillingService;
        _subscriberResolver = subscriberResolver;
    }

    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "Lists the authenticated user's subscriptions",
        Description = "Reads the caller's subscriptions from the billing system of record. Empty when the caller has never subscribed.",
        OperationId = "subscriptions.listMine",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<MySubscriptionsResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var response = new MySubscriptionsResponse();

        var subscriber = await _subscriberResolver.ResolveAsync(User, cancellationToken);
        if (subscriber is null)
        {
            return Unauthorized();
        }

        var subscriptions = await _subscriptionBillingService.ListSubscriptionsAsync(subscriber, cancellationToken);

        response.Subscriptions.AddRange(subscriptions.Select(s => s.ToDto()));
        response.HasActiveSubscription = subscriptions.Any(s => s.GrantsEntitlement);

        return Ok(response);
    }
}
