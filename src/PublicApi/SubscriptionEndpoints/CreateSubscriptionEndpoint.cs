using System;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a subscription plan.
/// Idempotent: subscribing to the same plan more than once returns the existing subscription.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
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
        Summary = "Subscribes the authenticated user to a plan",
        Description = "Ensures a Maxio customer exists for the user and subscribes them to the given plan. " +
                      "Subscribing to the same plan more than once returns the existing subscription.",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync();
        if (user is null)
        {
            return Unauthorized();
        }

        var subscriptionUser = new SubscriptionUser(
            user.Id,
            user.Email ?? user.UserName ?? string.Empty,
            FirstNameFromEmail(user.Email ?? user.UserName ?? string.Empty),
            "User");

        var subscription = await _subscriptionService.SubscribeAsync(subscriptionUser, request.PlanHandle, cancellationToken);

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = Map(subscription)
        };
        return Ok(response);
    }

    private async Task<ApplicationUser?> ResolveUserAsync()
    {
        var username = User.Identity?.Name;
        if (string.IsNullOrEmpty(username))
        {
            return null;
        }

        return await _userManager.FindByNameAsync(username);
    }

    private static string FirstNameFromEmail(string email)
    {
        var at = email.IndexOf('@');
        var local = at > 0 ? email[..at] : email;
        return string.IsNullOrWhiteSpace(local) ? "eShop" : local;
    }

    private static SubscriptionDto Map(Subscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        Price = subscription.Price,
        PriceInCents = subscription.PriceInCents,
        BalanceInCents = subscription.BalanceInCents,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextAssessmentAt = subscription.NextAssessmentAt,
        ActivatedAt = subscription.ActivatedAt,
        CreatedAt = subscription.CreatedAt,
        Reference = subscription.Reference
    };
}
