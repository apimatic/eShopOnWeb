using System;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a subscription plan. Ensures a Maxio Advanced Billing
/// customer exists for the user (idempotently) and never creates a duplicate subscription to
/// a plan the user already holds.
/// </summary>
[Authorize]
public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<CreateSubscriptionResponse>
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateSubscriptionEndpoint(ISubscriptionService subscriptionService, UserManager<ApplicationUser> userManager)
    {
        _subscriptionService = subscriptionService;
        _userManager = userManager;
    }

    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Subscribes to a plan",
        Description = "Ensures a Maxio customer exists for the authenticated user, then subscribes them to the requested plan. Idempotent: re-subscribing to a held plan returns the existing subscription.",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        if (request is null || string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return BadRequest(new { message = "planHandle is required." });
        }

        var userName = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Unauthorized();
        }

        var user = await _userManager.FindByNameAsync(userName);
        if (user is null)
        {
            return Unauthorized();
        }

        SubscribeResult result;
        try
        {
            result = await _subscriptionService.SubscribeAsync(user.Id, user.Email ?? userName, userName, request.PlanHandle, cancellationToken);
        }
        catch (SubscriptionPlanNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (MaxioApiException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway,
                new { message = "The billing service returned an error.", detail = ex.Message });
        }

        response.Created = result.Created;
        response.Subscription = new SubscriptionDto
        {
            MaxioSubscriptionId = result.Subscription.MaxioSubscriptionId,
            PlanHandle = result.Subscription.PlanHandle,
            PlanName = result.Subscription.PlanName,
            Price = result.Subscription.Price,
            Interval = result.Subscription.Interval,
            IntervalUnit = result.Subscription.IntervalUnit,
            State = result.Subscription.State,
            NextBillingDate = result.Subscription.NextBillingDate,
            CreatedAt = result.Subscription.CreatedAt
        };

        return response;
    }
}
