using System;
using System.Security.Claims;
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
/// Subscribes the signed-in shopper to a plan.
/// </summary>
public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<CreateSubscriptionResponse>
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly ILogger<CreateSubscriptionEndpoint> _logger;

    public CreateSubscriptionEndpoint(ISubscriptionService subscriptionService, ILogger<CreateSubscriptionEndpoint> logger)
    {
        _subscriptionService = subscriptionService;
        _logger = logger;
    }

    [Authorize]
    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Subscribes the signed-in shopper to a plan",
        Description = "Ensures a Maxio customer exists for the shopper and creates a subscription to the requested plan. " +
            "If the shopper already has a live subscription to the same plan, the existing subscription is returned " +
            "instead of creating a duplicate (HTTP 200 rather than 201).",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    [ProducesResponseType(typeof(CreateSubscriptionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CreateSubscriptionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(
        CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        string? email = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(email))
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return BadRequest();
        }

        try
        {
            var subscriber = new SubscriberProfile(email, request.FirstName, request.LastName);
            SubscriptionEnrollment enrollment = await _subscriptionService.SubscribeAsync(subscriber, request.PlanHandle, cancellationToken);

            var response = new CreateSubscriptionResponse
            {
                Created = !enrollment.AlreadyExisted,
                Subscription = enrollment.Subscription.ToSubscriptionDto()
            };

            return enrollment.AlreadyExisted
                ? Ok(response)
                : StatusCode(StatusCodes.Status201Created, response);
        }
        catch (Exception ex)
        {
            return SubscriptionErrorResponses.FromException(ex, _logger);
        }
    }
}
