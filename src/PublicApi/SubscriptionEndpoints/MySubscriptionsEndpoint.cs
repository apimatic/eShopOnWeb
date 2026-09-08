using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscriptions the authenticated shopper currently holds in the configured product family.
/// </summary>
public class MySubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<ListMySubscriptionsResponse>
{
    private readonly MaxioSubscriptionService _subscriptionService;
    private readonly UserManager<ApplicationUser> _userManager;

    public MySubscriptionsEndpoint(
        MaxioSubscriptionService subscriptionService,
        UserManager<ApplicationUser> userManager)
    {
        _subscriptionService = subscriptionService;
        _userManager = userManager;
    }

    [HttpGet("api/my-subscriptions")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [SwaggerOperation(
        Summary = "Lists the current user's subscriptions",
        Description = "Returns the subscriptions the authenticated shopper holds, as reported by Maxio Advanced Billing.",
        OperationId = "subscriptions.my",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<ListMySubscriptionsResponse>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        var response = new ListMySubscriptionsResponse();

        var userName = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Unauthorized();
        }

        var user = await _userManager.FindByNameAsync(userName);
        if (user == null)
        {
            return Unauthorized();
        }

        var subscriptions = await _subscriptionService.ListMySubscriptionsAsync(user, cancellationToken);
        response.Subscriptions.AddRange(subscriptions.Select(subscription => subscription.ToSubscriptionDto()));

        return Ok(response);
    }
}
