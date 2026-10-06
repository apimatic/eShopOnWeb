using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscriptions belonging to the current user.
/// </summary>
[Authorize]
public class ListMySubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<ListMySubscriptionsResponse>
{
    private readonly SubscriptionService _subscriptionService;
    private readonly UserManager<ApplicationUser> _userManager;

    public ListMySubscriptionsEndpoint(SubscriptionService subscriptionService, UserManager<ApplicationUser> userManager)
    {
        _subscriptionService = subscriptionService;
        _userManager = userManager;
    }

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "Lists the current user's subscriptions",
        Description = "Lists the subscriptions belonging to the current user in Maxio Advanced Billing",
        OperationId = "subscriptions.listMine",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<ListMySubscriptionsResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var response = new ListMySubscriptionsResponse();

        var user = await GetCurrentUserAsync();
        if (user is null)
        {
            return Unauthorized();
        }

        var subscriptions = await _subscriptionService.GetMySubscriptionsAsync(user, cancellationToken);
        response.Subscriptions.AddRange(subscriptions.Select(SubscriptionDtoMapper.ToSubscriptionDto));

        return response;
    }

    private async Task<ApplicationUser?> GetCurrentUserAsync()
    {
        var username = User.Identity?.Name;
        if (string.IsNullOrEmpty(username))
        {
            return null;
        }

        return await _userManager.FindByNameAsync(username);
    }
}
