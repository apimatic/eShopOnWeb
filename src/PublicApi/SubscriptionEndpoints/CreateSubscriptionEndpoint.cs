using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan, creating (or reusing) their Maxio
/// Advanced Billing customer. Idempotent: repeating the same request returns the existing
/// subscription instead of creating a duplicate.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<CreateSubscriptionResponse>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IMaxioBillingService _billingService;

    public CreateSubscriptionEndpoint(UserManager<ApplicationUser> userManager,
        IMaxioBillingService billingService)
    {
        _userManager = userManager;
        _billingService = billingService;
    }

    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Create a subscription",
        Description = "Subscribes the authenticated shopper to a plan",
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

        var outcome = await _billingService.SubscribeAsync(
            new BillingUser(user.Id, user.Email ?? user.UserName ?? string.Empty),
            request.ProductHandle ?? string.Empty,
            cancellationToken);

        var response = new CreateSubscriptionResponse
        {
            SubscriptionId = outcome.Subscription.SubscriptionId,
            CreatedNew = outcome.CreatedNew,
            MaxioCustomerId = outcome.MaxioCustomerId,
            State = outcome.Subscription.State,
            PlanHandle = outcome.Subscription.PlanHandle,
            PlanName = outcome.Subscription.PlanName,
            Price = outcome.Subscription.PriceInCents is int cents ? cents / 100m : null,
            PriceInCents = outcome.Subscription.PriceInCents,
            Currency = outcome.Subscription.Currency,
            NextBillingDateUtc = outcome.Subscription.NextBillingDateUtc,
            CreatedAtUtc = outcome.Subscription.CreatedAtUtc
        };

        return Ok(response);
    }

    private async Task<ApplicationUser?> ResolveUserAsync()
    {
        var userName = User.FindFirstValue(ClaimTypes.Name);

        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        return await _userManager.FindByNameAsync(userName);
    }
}
