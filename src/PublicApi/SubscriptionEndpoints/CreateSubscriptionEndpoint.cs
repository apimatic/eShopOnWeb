using System.Net;
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
public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<ShopperSubscription>
{
    private readonly ISubscriptionBillingService _billingService;
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateSubscriptionEndpoint(
        ISubscriptionBillingService billingService,
        UserManager<ApplicationUser> userManager)
    {
        _billingService = billingService;
        _userManager = userManager;
    }

    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Enrolls the authenticated shopper in a recurring-subscription plan",
        OperationId = "subscriptions.subscribe",
        Tags = new[] { "SubscriptionEndpoints" })]
    [ProducesResponseType(typeof(ShopperSubscription), (int)HttpStatusCode.Created)]
    [ProducesResponseType(typeof(ShopperSubscription), (int)HttpStatusCode.OK)]
    public override async Task<ActionResult<ShopperSubscription>> HandleAsync(
        CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var shopper = await ResolveShopperAsync();
        if (shopper is null)
        {
            return Unauthorized();
        }

        var result = await _billingService.SubscribeAsync(shopper, request.ProductHandle, cancellationToken);
        return result.Created
            ? Created("/api/my-subscriptions", result.Subscription)
            : Ok(result.Subscription);
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
