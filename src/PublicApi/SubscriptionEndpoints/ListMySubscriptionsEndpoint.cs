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
/// Lists the authenticated shopper's subscriptions as recorded in Maxio Advanced Billing.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class ListMySubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<ListMySubscriptionsResponse>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IMaxioBillingService _billingService;

    public ListMySubscriptionsEndpoint(UserManager<ApplicationUser> userManager,
        IMaxioBillingService billingService)
    {
        _userManager = userManager;
        _billingService = billingService;
    }

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "List my subscriptions",
        Description = "Lists the authenticated shopper's subscriptions",
        OperationId = "subscriptions.listMine",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<ListMySubscriptionsResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var userName = User.FindFirstValue(ClaimTypes.Name);
        var user = string.IsNullOrWhiteSpace(userName)
            ? null
            : await _userManager.FindByNameAsync(userName);

        if (user is null)
        {
            return Unauthorized();
        }

        var response = new ListMySubscriptionsResponse();

        var subscriptions = await _billingService.ListSubscriptionsForUserAsync(
            new BillingUser(user.Id, user.Email ?? user.UserName ?? string.Empty),
            cancellationToken);

        foreach (var subscription in subscriptions)
        {
            response.Subscriptions.Add(new SubscriptionSummaryDto
            {
                SubscriptionId = subscription.SubscriptionId,
                PlanHandle = subscription.PlanHandle,
                PlanName = subscription.PlanName,
                State = subscription.State,
                Price = subscription.PriceInCents is int cents ? cents / 100m : null,
                PriceInCents = subscription.PriceInCents,
                Currency = subscription.Currency,
                NextBillingDateUtc = subscription.NextBillingDateUtc,
                CreatedAtUtc = subscription.CreatedAtUtc,
                CanceledAtUtc = subscription.CanceledAtUtc
            });
        }

        return Ok(response);
    }
}
