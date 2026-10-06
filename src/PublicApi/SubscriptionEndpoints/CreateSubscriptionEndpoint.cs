using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan. Idempotent: a shopper who already has a
/// live subscription to the requested plan gets that subscription back instead of a duplicate.
/// </summary>
public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<CreateSubscriptionResponse>
{
    private readonly ISubscriptionService _subscriptionService;

    public CreateSubscriptionEndpoint(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    [HttpPost("api/subscriptions")]
    [Authorize]
    [SwaggerOperation(
        Summary = "Subscribes the authenticated shopper to a plan",
        Description = "Subscribes the authenticated shopper to a plan. Idempotent: a shopper who already has a live subscription to the requested plan gets that subscription back instead of a duplicate.",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var customerReference = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(customerReference))
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return BadRequest(new { message = "PlanHandle is required." });
        }

        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var result = await _subscriptionService.SubscribeAsync(customerReference, customerReference, request.PlanHandle, cancellationToken);

        response.Subscription = new SubscriptionDto
        {
            Id = result.Subscription.Id,
            State = result.Subscription.State,
            CustomerId = result.Subscription.CustomerId,
            CustomerReference = result.Subscription.CustomerReference,
            PlanHandle = result.Subscription.PlanHandle,
            PlanName = result.Subscription.PlanName,
            ProductPriceInCents = result.Subscription.ProductPriceInCents,
            CurrentPeriodEndsAt = result.Subscription.CurrentPeriodEndsAt,
            NextAssessmentAt = result.Subscription.NextAssessmentAt,
            ActivatedAt = result.Subscription.ActivatedAt,
            CreatedAt = result.Subscription.CreatedAt,
            PaymentCollectionMethod = result.Subscription.PaymentCollectionMethod
        };
        response.Created = result.Created;

        return result.Created ? StatusCode(StatusCodes.Status201Created, response) : Ok(response);
    }
}
