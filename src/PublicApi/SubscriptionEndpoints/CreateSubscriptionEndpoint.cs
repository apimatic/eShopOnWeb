using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Subscription;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a plan
/// </summary>
[Authorize]
public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<CreateSubscriptionResponse>
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateSubscriptionEndpoint(ISubscriptionService subscriptionService,
        UserManager<ApplicationUser> userManager)
    {
        _subscriptionService = subscriptionService;
        _userManager = userManager;
    }

    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Subscribes the authenticated user to a plan",
        Description = "Ensures a billing-system customer exists for the user (idempotent) and enrolls them in the plan identified by productHandle",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var userContext = await ResolveUserAsync(cancellationToken);
        if (userContext is null)
        {
            return Unauthorized();
        }

        var subscription = await _subscriptionService.SubscribeAsync(userContext, request.ProductHandle, cancellationToken);

        response.Subscription = new SubscriptionDto
        {
            SubscriptionId = subscription.SubscriptionId,
            State = subscription.State,
            PlanHandle = subscription.PlanHandle,
            PlanName = subscription.PlanName,
            Price = subscription.Price,
            Currency = subscription.Currency,
            Interval = subscription.Interval,
            IntervalUnit = subscription.IntervalUnit,
            NextBillingDate = subscription.NextBillingDate,
            CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt
        };

        return response;
    }

    private async Task<SubscriptionUserContext?> ResolveUserAsync(CancellationToken cancellationToken)
    {
        var userName = User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(userName))
        {
            return null;
        }

        var user = await _userManager.FindByNameAsync(userName);
        if (user is null)
        {
            return null;
        }

        return new SubscriptionUserContext(user.Id, user.UserName ?? userName, user.Email ?? userName);
    }
}