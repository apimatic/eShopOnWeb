using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated user's subscriptions, reconciled with Maxio.
/// </summary>
[Authorize]
public class MySubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<MySubscriptionsResponse>
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly UserManager<ApplicationUser> _userManager;

    public MySubscriptionsEndpoint(ISubscriptionService subscriptionService, UserManager<ApplicationUser> userManager)
    {
        _subscriptionService = subscriptionService;
        _userManager = userManager;
    }

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "Lists the current user's subscriptions",
        Description = "Lists the authenticated user's subscriptions with live state from Maxio",
        OperationId = "subscription.my.list",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<MySubscriptionsResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var response = new MySubscriptionsResponse(Guid.NewGuid());

        var user = await _userManager.FindByNameAsync(User.Identity!.Name!);
        if (user is null)
            return Unauthorized();

        var summaries = await _subscriptionService.ListForUserAsync(user.Id, cancellationToken);
        response.Subscriptions = summaries.Select(SubscriptionPlanListEndpoint.ToDto).ToList();
        return response;
    }
}
