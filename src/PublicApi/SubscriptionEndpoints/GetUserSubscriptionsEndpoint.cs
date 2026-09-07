using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Services;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetUserSubscriptionsEndpoint : EndpointBaseAsync.WithoutRequest.WithActionResult<GetUserSubscriptionsResponse>
{
    private readonly IMaxioSubscriptionService _subscriptionService;
    private readonly UserManager<ApplicationUser> _userManager;

    public GetUserSubscriptionsEndpoint(
        IMaxioSubscriptionService subscriptionService,
        UserManager<ApplicationUser> userManager)
    {
        _subscriptionService = subscriptionService;
        _userManager = userManager;
    }

    [HttpGet("api/my-subscriptions")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [SwaggerOperation(
        Summary = "Get subscriptions for the authenticated user",
        Description = "Get subscriptions for the authenticated user",
        OperationId = "subscriptions.list",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<GetUserSubscriptionsResponse>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        var userName = User.FindFirst(ClaimTypes.Name)?.Value;
        if (string.IsNullOrEmpty(userName))
        {
            return Unauthorized();
        }

        var appUser = await _userManager.FindByNameAsync(userName);
        if (appUser == null)
        {
            return Unauthorized();
        }

        var subscriptions = await _subscriptionService.GetUserSubscriptionsAsync(
            appUser.Id, appUser.Email ?? string.Empty);

        var response = new GetUserSubscriptionsResponse
        {
            Subscriptions = subscriptions
        };

        return Ok(response);
    }
}

public class GetUserSubscriptionsResponse
{
    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}
