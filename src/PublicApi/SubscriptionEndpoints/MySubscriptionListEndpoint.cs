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
/// Lists the authenticated user's subscriptions.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class MySubscriptionListEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<MySubscriptionListResponse>
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly UserManager<ApplicationUser> _userManager;

    public MySubscriptionListEndpoint(ISubscriptionService subscriptionService, UserManager<ApplicationUser> userManager)
    {
        _subscriptionService = subscriptionService;
        _userManager = userManager;
    }

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "List my subscriptions",
        Description = "Lists the subscriptions of the authenticated user",
        OperationId = "subscriptions.listMine",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<MySubscriptionListResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var userName = User?.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName) || await _userManager.FindByNameAsync(userName) is null)
        {
            return Unauthorized();
        }

        var subscriptions = await _subscriptionService.ListSubscriptionsForUserAsync(userName, cancellationToken);

        var response = new MySubscriptionListResponse
        {
            Subscriptions = subscriptions.Select(CreateSubscriptionEndpoint.ToDto).ToList()
        };

        return Ok(response);
    }
}
