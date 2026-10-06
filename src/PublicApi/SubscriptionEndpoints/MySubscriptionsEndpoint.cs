using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscriptions belonging to the authenticated shopper.
/// </summary>
public class MySubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<MySubscriptionsResponse>
{
    private readonly ISubscriptionService _subscriptionService;

    public MySubscriptionsEndpoint(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    [HttpGet("api/my-subscriptions")]
    [Authorize]
    [SwaggerOperation(
        Summary = "Lists the subscriptions belonging to the authenticated shopper",
        Description = "Lists the subscriptions belonging to the authenticated shopper",
        OperationId = "subscriptions.mine",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<MySubscriptionsResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var customerReference = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(customerReference))
        {
            return Unauthorized();
        }

        var response = new MySubscriptionsResponse();

        var subscriptions = await _subscriptionService.ListSubscriptionsAsync(customerReference, cancellationToken);

        response.Subscriptions.AddRange(subscriptions.Select(subscription => new SubscriptionDto
        {
            Id = subscription.Id,
            State = subscription.State,
            CustomerId = subscription.CustomerId,
            CustomerReference = subscription.CustomerReference,
            PlanHandle = subscription.PlanHandle,
            PlanName = subscription.PlanName,
            ProductPriceInCents = subscription.ProductPriceInCents,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            NextAssessmentAt = subscription.NextAssessmentAt,
            ActivatedAt = subscription.ActivatedAt,
            CreatedAt = subscription.CreatedAt,
            PaymentCollectionMethod = subscription.PaymentCollectionMethod
        }));

        return Ok(response);
    }
}
