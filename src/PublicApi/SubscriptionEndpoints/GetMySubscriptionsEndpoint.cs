using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated user's Maxio subscriptions (plan, price, state, next billing date).
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class GetMySubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<GetMySubscriptionsResponse>
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly UserManager<ApplicationUser> _userManager;

    public GetMySubscriptionsEndpoint(ISubscriptionService subscriptionService, UserManager<ApplicationUser> userManager)
    {
        _subscriptionService = subscriptionService;
        _userManager = userManager;
    }

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "Lists the user's subscriptions",
        Description = "Lists the user's Maxio subscriptions with plan, price, state and next billing date.",
        OperationId = "subscriptions.listMine",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<GetMySubscriptionsResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var response = new GetMySubscriptionsResponse();

        // The JWT carries the username as ClaimTypes.Name.
        var username = User.Identity?.Name;
        var appUser = string.IsNullOrEmpty(username) ? null : await _userManager.FindByNameAsync(username);
        if (appUser is null || string.IsNullOrEmpty(appUser.Id))
        {
            return Problem(title: "The authenticated user could not be resolved.", statusCode: StatusCodes.Status401Unauthorized);
        }

        response.Subscriptions.AddRange(await _subscriptionService.GetSubscriptionsAsync(appUser.Id, cancellationToken));
        return Ok(response);
    }
}