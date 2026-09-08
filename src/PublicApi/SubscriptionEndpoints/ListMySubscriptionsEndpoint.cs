using System;
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
/// Lists the authenticated shopper's subscriptions.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class ListMySubscriptionsEndpoint : EndpointBaseAsync
    .WithRequest<ListMySubscriptionsRequest>
    .WithActionResult<ListMySubscriptionsResponse>
{
    private readonly IMaxioBillingService _maxioBillingService;
    private readonly UserManager<ApplicationUser> _userManager;

    public ListMySubscriptionsEndpoint(IMaxioBillingService maxioBillingService, UserManager<ApplicationUser> userManager)
    {
        _maxioBillingService = maxioBillingService;
        _userManager = userManager;
    }

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "List the current shopper's subscriptions",
        Description = "Lists the subscriptions the authenticated shopper holds in the configured product family.",
        OperationId = "subscriptions.my",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<ListMySubscriptionsResponse>> HandleAsync(
        [FromQuery] ListMySubscriptionsRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = new ListMySubscriptionsResponse(request.CorrelationId());

        var userName = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new UnauthorizedAccessException("A valid bearer token identifying the shopper is required.");
        }

        var shopper = await _userManager.FindByNameAsync(userName);
        if (shopper == null)
        {
            throw new UnauthorizedAccessException($"No shopper account exists for '{userName}'.");
        }

        var customerReference = shopper.UserName ?? shopper.Email;
        var subscriptions = await _maxioBillingService.GetSubscriptionsForCustomerAsync(customerReference ?? string.Empty, cancellationToken);

        response.Subscriptions.AddRange(subscriptions.Select(SubscriptionMapping.ToSubscriptionDto));

        return Ok(response);
    }
}
