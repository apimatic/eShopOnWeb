using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan.
/// </summary>
public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<CreateSubscriptionResponse>
{
    private readonly ISubscriptionBillingService _subscriptionBillingService;
    private readonly ISubscriberIdentityAccessor _subscriberIdentityAccessor;
    private readonly ILogger<CreateSubscriptionEndpoint> _logger;

    public CreateSubscriptionEndpoint(ISubscriptionBillingService subscriptionBillingService,
        ISubscriberIdentityAccessor subscriberIdentityAccessor,
        ILogger<CreateSubscriptionEndpoint> logger)
    {
        _subscriptionBillingService = subscriptionBillingService;
        _subscriberIdentityAccessor = subscriberIdentityAccessor;
        _logger = logger;
    }

    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Subscribes the authenticated shopper to a plan",
        Description = "Ensures a billing customer exists for the caller and enrolls them in the requested plan. " +
                      "Idempotent: if a live subscription to the same plan already exists it is returned with " +
                      "alreadySubscribed = true and nothing new is created.",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    [ProducesResponseType(typeof(CreateSubscriptionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(CreateSubscriptionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(
        CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request?.PlanHandle))
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "planHandle is required.",
                Detail = "Send the handle of a plan from GET /api/subscription-plans."
            });
        }

        var subscriber = await _subscriberIdentityAccessor.ResolveAsync(User);

        if (subscriber is null)
        {
            // The token authenticated but no matching user exists, e.g. after an identity reset.
            return Unauthorized();
        }

        try
        {
            var enrollment = await _subscriptionBillingService.SubscribeAsync(subscriber, request.PlanHandle,
                cancellationToken);

            var plan = await _subscriptionBillingService.FindPlanAsync(request.PlanHandle, cancellationToken);

            var response = new CreateSubscriptionResponse(request.CorrelationId())
            {
                Subscription = SubscriptionDto.FromSubscription(enrollment.Subscription),
                Plan = plan is null ? null : SubscriptionPlanDto.FromPlan(plan),
                AlreadySubscribed = enrollment.AlreadyEnrolled,
                CustomerId = enrollment.Customer.Id,
                CustomerReference = enrollment.Customer.Reference
            };

            if (enrollment.AlreadyEnrolled)
            {
                // Nothing was created, so this is a 200 rather than a 201 - which is exactly what
                // a double-clicked subscribe should look like to the caller.
                return Ok(response);
            }

            _logger.LogInformation("Shopper {User} subscribed to {PlanHandle} (subscription {SubscriptionId}).",
                subscriber.UserId, request.PlanHandle, enrollment.Subscription.Id);

            return Created("api/my-subscriptions", response);
        }
        catch (SubscriptionPlanNotFoundException ex)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Unknown subscription plan.",
                Detail = $"{ex.Message} Use a handle from GET /api/subscription-plans."
            });
        }
    }
}
