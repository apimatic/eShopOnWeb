using System;
using System.Collections.Generic;
using System.Linq;
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
using Microsoft.Extensions.Logging;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the Maxio subscriptions belonging to the authenticated shopper.
/// </summary>
public class MySubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<MySubscriptionsResponse>
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<MySubscriptionsEndpoint> _logger;

    public MySubscriptionsEndpoint(ISubscriptionService subscriptionService,
        UserManager<ApplicationUser> userManager,
        ILogger<MySubscriptionsEndpoint> logger)
    {
        _subscriptionService = subscriptionService;
        _userManager = userManager;
        _logger = logger;
    }

    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "Lists the authenticated shopper's subscriptions",
        Description = "Lists the subscriptions the authenticated shopper has on the billing system",
        OperationId = "subscriptions.my-subscriptions",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    [ProducesResponseType(typeof(MySubscriptionsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public override async Task<ActionResult<MySubscriptionsResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var user = await SubscriptionUserResolver.GetAuthenticatedUserAsync(User, _userManager);
        if (user is null)
        {
            _logger.LogWarning("Could not resolve the authenticated token subject to an eShop user.");
            return Unauthorized();
        }

        try
        {
            var subscriptions = await _subscriptionService.GetSubscriptionsForUserAsync(user, cancellationToken);
            var response = new MySubscriptionsResponse
            {
                Subscriptions = subscriptions.ToList()
            };
            return Ok(response);
        }
        catch (Exception ex) when (ex is MaxioConfigurationException or MaxioApiException)
        {
            return SubscriptionEndpointErrorMapper.ToObjectResult(ex, _logger);
        }
    }
}
