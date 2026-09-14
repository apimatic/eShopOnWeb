using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan.
///
/// The subscriber is taken from the bearer token, never from the request body, so a caller cannot
/// enroll somebody else. The billing customer is created on first use, and the whole operation is
/// idempotent: a double submit answers 200 with the subscription that already exists instead of
/// creating a second one.
/// </summary>
public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<CreateSubscriptionResponse>
{
    private readonly ISubscriptionBillingService _subscriptionBillingService;
    private readonly ISubscriberResolver _subscriberResolver;

    public CreateSubscriptionEndpoint(
        ISubscriptionBillingService subscriptionBillingService,
        ISubscriberResolver subscriberResolver)
    {
        _subscriptionBillingService = subscriptionBillingService;
        _subscriberResolver = subscriberResolver;
    }

    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Subscribes the authenticated user to a plan",
        Description = "Ensures a billing customer exists for the caller and enrolls them in the requested plan. " +
                      "Idempotent: repeating the request returns the existing subscription with 200 instead of creating a duplicate.",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(
        [FromBody] CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var subscriber = await _subscriberResolver.ResolveAsync(User, cancellationToken);
        if (subscriber is null)
        {
            return Unauthorized();
        }

        var result = await _subscriptionBillingService.SubscribeAsync(
            new SubscribeRequest(subscriber, request.PlanHandle, request.IdempotencyKey),
            cancellationToken);

        response.Created = result.Outcome == SubscribeOutcome.Created;
        response.Subscription = result.Subscription.ToDto();

        return response.Created
            ? Created($"api/subscriptions/{result.Subscription.Id}", response)
            : Ok(response);
    }
}
