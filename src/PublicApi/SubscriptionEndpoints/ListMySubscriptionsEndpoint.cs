using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.SubscriptionBilling;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[Controller]
[Authorize]
public class ListMySubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<List<ShopperSubscription>>
{
    private readonly ISubscriptionBillingService _billingService;
    private readonly UserManager<ApplicationUser> _userManager;

    public ListMySubscriptionsEndpoint(
        ISubscriptionBillingService billingService,
        UserManager<ApplicationUser> userManager)
    {
        _billingService = billingService;
        _userManager = userManager;
    }

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "Lists the authenticated shopper's subscriptions",
        OperationId = "subscriptions.listMine",
        Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<List<ShopperSubscription>>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var shopper = await ResolveShopperAsync();
        if (shopper is null)
        {
            return Unauthorized();
        }

        var subscriptions = await _billingService.ListSubscriptionsAsync(shopper, cancellationToken);
        return Ok(subscriptions);
    }

    private async Task<Shopper?> ResolveShopperAsync()
    {
        var username = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
        {
            return null;
        }

        var user = await _userManager.FindByNameAsync(username);
        var email = user?.Email ?? user?.UserName;
        return user is null || string.IsNullOrWhiteSpace(email) ? null : new Shopper(user.Id, email);
    }
}
