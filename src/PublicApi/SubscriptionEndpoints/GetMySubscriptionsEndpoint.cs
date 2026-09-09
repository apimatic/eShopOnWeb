using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Returns the authenticated user's Maxio subscriptions (live from Maxio,
/// merged with the local cross-reference).
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class GetMySubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<GetMySubscriptionsResponse>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ISubscriptionService _subscriptionService;

    public GetMySubscriptionsEndpoint(UserManager<ApplicationUser> userManager,
        ISubscriptionService subscriptionService)
    {
        _userManager = userManager;
        _subscriptionService = subscriptionService;
    }

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "Lists the authenticated user's subscriptions",
        Description = "Lists the Maxio subscriptions of the authenticated user",
        OperationId = "subscriptions.my",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<GetMySubscriptionsResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var username = User.Identity?.Name;
        if (string.IsNullOrEmpty(username))
        {
            return Unauthorized();
        }

        var user = await _userManager.FindByNameAsync(username);
        if (user == null)
        {
            return Unauthorized();
        }

        try
        {
            var subscriptions = await _subscriptionService.GetMySubscriptionsAsync(user.Id, cancellationToken);
            return new GetMySubscriptionsResponse(Guid.NewGuid())
            {
                Subscriptions = subscriptions.ToList()
            };
        }
        catch (MaxioApiException ex)
        {
            return Problem(title: "Maxio subscription lookup failed", detail: ex.Message, statusCode: 502);
        }
    }
}
