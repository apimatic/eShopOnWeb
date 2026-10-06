using Ardalis.ApiEndpoints;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated shopper's subscriptions from the billing system.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class ListMySubscriptionsEndpoint : EndpointBaseAsync
    .WithRequest<ListMySubscriptionsRequest>
    .WithActionResult<ListMySubscriptionsResponse>
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly UserManager<ApplicationUser> _userManager;

    public ListMySubscriptionsEndpoint(
        ISubscriptionService subscriptionService,
        UserManager<ApplicationUser> userManager)
    {
        _subscriptionService = subscriptionService;
        _userManager = userManager;
    }

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "Lists the current shopper's subscriptions",
        Description = "Returns subscriptions owned by the authenticated user's Maxio customer, " +
                      "empty when the user has never subscribed.",
        OperationId = "subscriptions.listMine",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<ListMySubscriptionsResponse>> HandleAsync(
        [FromQuery] ListMySubscriptionsRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = new ListMySubscriptionsResponse(request.CorrelationId());

        var username = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
        {
            return Unauthorized();
        }

        var shopper = await _userManager.FindByNameAsync(username);
        if (shopper is null)
        {
            return Unauthorized();
        }

        var subscriptions = await _subscriptionService
            .GetSubscriptionsForUserAsync(shopper.Id, cancellationToken);

        foreach (var subscription in subscriptions)
        {
            response.Subscriptions.Add(SubscriptionDto.From(subscription));
        }

        return Ok(response);
    }
}
