using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated user's subscriptions as recorded in Maxio.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class GetMySubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<GetMySubscriptionsResponse>
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly UserManager<ApplicationUser> _userManager;

    public GetMySubscriptionsEndpoint(ISubscriptionService subscriptionService,
        UserManager<ApplicationUser> userManager)
    {
        _subscriptionService = subscriptionService;
        _userManager = userManager;
    }

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "Lists the user's subscriptions",
        Description = "Lists the subscriptions the authenticated user holds in Maxio Advanced Billing, including plan, price, state and next billing date",
        OperationId = "subscriptions.mySubscriptions",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<GetMySubscriptionsResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var username = User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(username))
        {
            return Unauthorized();
        }

        var user = await _userManager.FindByNameAsync(username);
        if (user == null)
        {
            return Unauthorized();
        }

        var subscriptions = await _subscriptionService.GetSubscriptionsForUserAsync(user.Id, cancellationToken);

        var response = new GetMySubscriptionsResponse();
        response.Subscriptions.AddRange(subscriptions.Select(s => new SubscriptionDto
        {
            SubscriptionId = s.Id,
            PlanHandle = s.Product?.Handle ?? string.Empty,
            PlanName = s.Product?.Name ?? string.Empty,
            State = s.State,
            PriceInCents = s.ProductPriceInCents ?? s.Product?.PriceInCents ?? 0,
            Price = ListSubscriptionPlansEndpoint.FormatPrice(s.ProductPriceInCents ?? s.Product?.PriceInCents ?? 0),
            IntervalUnit = s.Product?.IntervalUnit ?? string.Empty,
            NextBillingDate = s.CurrentPeriodEndsAt,
            CreatedAt = s.CreatedAt,
        }));

        return Ok(response);
    }
}