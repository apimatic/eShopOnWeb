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
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscriptions of the authenticated user.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class ListMySubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<ListMySubscriptionsResponse>
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly UserManager<ApplicationUser> _userManager;

    public ListMySubscriptionsEndpoint(ISubscriptionService subscriptionService,
        UserManager<ApplicationUser> userManager)
    {
        _subscriptionService = subscriptionService;
        _userManager = userManager;
    }

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "Lists the authenticated user's subscriptions",
        Description = "Lists the subscriptions of the authenticated user, with state refreshed from the billing system of record",
        OperationId = "subscriptions.my",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<ListMySubscriptionsResponse>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        var name = User?.Identity?.Name;
        if (string.IsNullOrEmpty(name))
        {
            return Unauthorized();
        }

        var user = await _userManager.FindByNameAsync(name);
        if (user is null)
        {
            return Unauthorized();
        }

        var subscriptions = await _subscriptionService.GetSubscriptionsForUserAsync(user.Id, cancellationToken);
        var response = new ListMySubscriptionsResponse
        {
            CorrelationId = Guid.NewGuid().ToString()
        };
        response.Subscriptions.AddRange(subscriptions.Select(CreateSubscriptionEndpoint.Map));

        return Ok(response);
    }
}
