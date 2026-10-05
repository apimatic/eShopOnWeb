using System;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.eShopWeb.PublicApi.Services;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// The stable handle of the plan to subscribe to (e.g. "eshop-pro").
    /// Required unless PlanId is supplied.
    /// </summary>
    public string? PlanHandle { get; set; }

    /// <summary>
    /// Alternative to PlanHandle: the numeric Maxio product id.
    /// Note that Maxio reassigns numeric ids on re-seed; prefer handles.
    /// </summary>
    public int? PlanId { get; set; }
}

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId) { }
    public CreateSubscriptionResponse() { }

    public SubscriptionDto Subscription { get; set; } = new();
    /// <summary>
    /// True when the caller was already subscribed to the plan and the
    /// existing subscription was returned instead of creating a new one.
    /// </summary>
    public bool AlreadySubscribed { get; set; }
}

/// <summary>
/// Subscribes the authenticated user to a subscription plan.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
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
    [SwaggerOperation(
        Summary = "Subscribes the current user to a plan",
        Description = "Ensures a billing-system customer exists for the user, then subscribes them to the requested plan. Idempotent: re-subscribing to a held plan returns the existing subscription.",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(
        CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null || (string.IsNullOrWhiteSpace(request.PlanHandle) && !request.PlanId.HasValue))
        {
            return BadRequest("Either planHandle or planId must be supplied.");
        }

        var user = await _subscriptionService.ResolveUserAsync(User, cancellationToken);
        if (user == null)
        {
            return Unauthorized();
        }

        try
        {
            var (subscription, alreadySubscribed) =
                await _subscriptionService.SubscribeAsync(user, request.PlanHandle, request.PlanId, cancellationToken);
            var response = new CreateSubscriptionResponse(request.CorrelationId())
            {
                Subscription = subscription,
                AlreadySubscribed = alreadySubscribed
            };
            return Ok(response);
        }
        catch (PlanNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (MaxioApiException ex)
        {
            return Problem(
                title: "Billing system error",
                detail: ex.Message,
                statusCode: 502);
        }
    }
}