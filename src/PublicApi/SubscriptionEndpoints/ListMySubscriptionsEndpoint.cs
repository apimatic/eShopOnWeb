using System;
using System.Linq;
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
/// Lists the subscriptions of the authenticated user
/// </summary>
[Authorize]
public class ListMySubscriptionsEndpoint : EndpointBaseAsync
    .WithRequest<ListMySubscriptionsRequest>
    .WithActionResult<ListMySubscriptionsResponse>
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly UserManager<ApplicationUser> _userManager;

    public ListMySubscriptionsEndpoint(ISubscriptionService subscriptionService,
        UserManager<ApplicationUser> userManager)
    {
        _subscriptionService = subscriptionService;
        _userManager = userManager;
    }

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "Lists the subscriptions of the authenticated user",
        Description = "Lists the subscriptions the authenticated user holds in the billing system",
        OperationId = "subscriptions.listMine",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<ListMySubscriptionsResponse>> HandleAsync([FromQuery] ListMySubscriptionsRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = new ListMySubscriptionsResponse(request.CorrelationId());

        var userName = User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(userName))
        {
            return Unauthorized();
        }

        var user = await _userManager.FindByNameAsync(userName);
        if (user is null)
        {
            return Unauthorized();
        }

        var userContext = new SubscriptionUserContext(user.Id, user.UserName ?? userName, user.Email ?? userName);

        var subscriptions = await _subscriptionService.GetSubscriptionsForUserAsync(userContext, cancellationToken);

        response.Subscriptions.AddRange(subscriptions.Select(s => new SubscriptionDto
        {
            SubscriptionId = s.SubscriptionId,
            State = s.State,
            PlanHandle = s.PlanHandle,
            PlanName = s.PlanName,
            Price = s.Price,
            Currency = s.Currency,
            Interval = s.Interval,
            IntervalUnit = s.IntervalUnit,
            NextBillingDate = s.NextBillingDate,
            CurrentPeriodStartedAt = s.CurrentPeriodStartedAt
        }));

        return response;
    }
}