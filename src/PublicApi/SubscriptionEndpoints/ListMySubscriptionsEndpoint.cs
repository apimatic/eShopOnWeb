using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated shopper's Maxio subscriptions.
/// </summary>
public class ListMySubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<ListMySubscriptionsResponse>
{
    private readonly ISubscriptionBillingService _billing;
    private readonly UserManager<ApplicationUser> _userManager;

    public ListMySubscriptionsEndpoint(
        ISubscriptionBillingService billing,
        UserManager<ApplicationUser> userManager)
    {
        _billing = billing;
        _userManager = userManager;
    }

    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "List my subscriptions",
        Description = "Returns Maxio subscriptions for the authenticated shopper.",
        OperationId = "subscriptions.listMine",
        Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<ListMySubscriptionsResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var shopper = await ShopperIdentityFactory.FromUserAsync(_userManager, User, cancellationToken);
        if (shopper is null)
        {
            return Unauthorized();
        }

        var subscriptions = await _billing.ListMySubscriptionsAsync(shopper, cancellationToken);
        var response = new ListMySubscriptionsResponse
        {
            Subscriptions = subscriptions.Select(ShopperSubscriptionDto.From).ToList()
        };
        return Ok(response);
    }
}
