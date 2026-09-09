using System;
using System.Linq;
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
/// Lists the current user's subscriptions with live plan, price, state and
/// next-billing-date from Maxio.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class ListMySubscriptionsEndpoint : EndpointBaseAsync
    .WithRequest<ListMySubscriptionsRequest>
    .WithActionResult<ListMySubscriptionsResponse>
{
    private readonly ISubscriptionManager _subscriptionManager;
    private readonly UserManager<ApplicationUser> _userManager;

    public ListMySubscriptionsEndpoint(ISubscriptionManager subscriptionManager,
        UserManager<ApplicationUser> userManager)
    {
        _subscriptionManager = subscriptionManager;
        _userManager = userManager;
    }

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "Lists the current user's subscriptions",
        Description = "Lists the current user's subscriptions hydrated with live Maxio state",
        OperationId = "subscriptions.mySubscriptions",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<ListMySubscriptionsResponse>> HandleAsync(
        [FromQuery] ListMySubscriptionsRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByNameAsync(User.Identity?.Name ?? string.Empty);
        if (user is null)
        {
            return Unauthorized();
        }

        var response = new ListMySubscriptionsResponse(request.CorrelationId());
        var subscriptions = await _subscriptionManager.GetSubscriptionsForUserAsync(user.Id, cancellationToken);
        response.Subscriptions.AddRange(subscriptions.Select(s => new SubscriptionDto
        {
            Id = s.MaxioSubscriptionId,
            State = s.State,
            PlanHandle = s.PlanHandle,
            PlanName = s.PlanName,
            PriceInCents = s.PriceInCents,
            Price = s.PriceInCents / 100m,
            Currency = s.Currency,
            NextBillingDate = s.NextBillingDate,
            ActivatedAt = s.ActivatedAt,
            Stale = s.Stale
        }));

        return Ok(response);
    }
}
