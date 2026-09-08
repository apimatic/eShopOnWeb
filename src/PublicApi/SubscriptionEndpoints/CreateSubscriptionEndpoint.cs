using System;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Subscription;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated caller to a subscription plan. Ensures a billing-system
/// customer exists for the user (idempotently) and enrolls them; a repeated call for the
/// same user and plan returns the existing subscription instead of creating a duplicate.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<CreateSubscriptionResponse>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ISubscriptionBillingService _subscriptionBillingService;

    public CreateSubscriptionEndpoint(
        UserManager<ApplicationUser> userManager,
        ISubscriptionBillingService subscriptionBillingService)
    {
        _userManager = userManager;
        _subscriptionBillingService = subscriptionBillingService;
    }

    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Subscribes the caller to a plan",
        Description = "Ensures a billing customer exists for the caller and enrolls them in the requested subscription plan",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(
        CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var user = await SubscriptionUserResolver.ResolveUserAsync(User, _userManager);
        if (user is null)
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle) && !request.PlanId.HasValue)
        {
            return BadRequest(new { message = "Provide planHandle or planId." });
        }

        var command = new SubscribeCommand(
            new SubscriberInfo(user.Id, user.Email ?? user.UserName ?? string.Empty),
            request.PlanHandle,
            request.PlanId);

        try
        {
            var subscription = await _subscriptionBillingService.SubscribeAsync(command, cancellationToken);
            response.Subscription = new SubscriptionDto
            {
                Id = subscription.Id,
                Reference = subscription.Reference,
                State = subscription.State,
                PlanHandle = subscription.PlanHandle,
                PlanName = subscription.PlanName,
                Price = subscription.Price,
                Currency = subscription.Currency,
                NextBillingDate = subscription.NextBillingDate,
                ActivatedAt = subscription.ActivatedAt,
                CreatedAt = subscription.CreatedAt
            };
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }

        return Ok(response);
    }
}
