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
/// Lists the subscriptions belonging to the authenticated user.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class ListMySubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<ListMySubscriptionsResponse>
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly UserManager<ApplicationUser> _userManager;

    public ListMySubscriptionsEndpoint(ISubscriptionService subscriptionService, UserManager<ApplicationUser> userManager)
    {
        _subscriptionService = subscriptionService;
        _userManager = userManager;
    }

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "Lists the authenticated user's subscriptions",
        Description = "Lists the subscriptions belonging to the authenticated user",
        OperationId = "subscriptions.listMine",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<ListMySubscriptionsResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var username = User.Identity?.Name;
        if (string.IsNullOrEmpty(username))
        {
            return Unauthorized();
        }

        var user = await _userManager.FindByNameAsync(username);
        if (user is null)
        {
            return Unauthorized();
        }

        var subscriptions = await _subscriptionService.ListMySubscriptionsAsync(user.Id, cancellationToken);

        var response = new ListMySubscriptionsResponse();
        foreach (var subscription in subscriptions)
        {
            response.Subscriptions.Add(new SubscriptionDto
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
            });
        }

        return Ok(response);
    }
}
