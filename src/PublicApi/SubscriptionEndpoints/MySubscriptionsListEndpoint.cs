using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Logging;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscriptions belonging to the signed-in shopper.
/// </summary>
public class MySubscriptionsListEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<MySubscriptionsListResponse>
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly ILogger<MySubscriptionsListEndpoint> _logger;

    public MySubscriptionsListEndpoint(ISubscriptionService subscriptionService, ILogger<MySubscriptionsListEndpoint> logger)
    {
        _subscriptionService = subscriptionService;
        _logger = logger;
    }

    [Authorize]
    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "Lists the signed-in shopper's subscriptions",
        Description = "Returns every Maxio subscription for the signed-in shopper, including plan, price, state and next billing date.",
        OperationId = "subscriptions.mine",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    [ProducesResponseType(typeof(MySubscriptionsListResponse), StatusCodes.Status200OK)]
    public override async Task<ActionResult<MySubscriptionsListResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        string? email = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(email))
        {
            return Unauthorized();
        }

        var response = new MySubscriptionsListResponse();

        try
        {
            IReadOnlyList<MaxioSubscription> subscriptions = await _subscriptionService.ListSubscriptionsAsync(email, cancellationToken);
            foreach (MaxioSubscription subscription in subscriptions)
            {
                response.Subscriptions.Add(subscription.ToSubscriptionDto());
            }

            return Ok(response);
        }
        catch (Exception ex)
        {
            return SubscriptionErrorResponses.FromException(ex, _logger);
        }
    }
}
