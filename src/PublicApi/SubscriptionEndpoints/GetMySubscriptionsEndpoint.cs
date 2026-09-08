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
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated user's Maxio subscriptions
/// </summary>
public class GetMySubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<GetMySubscriptionsResponse>
{
    private readonly IMaxioSubscriptionService _subscriptionService;
    private readonly UserManager<ApplicationUser> _userManager;

    public GetMySubscriptionsEndpoint(IMaxioSubscriptionService subscriptionService,
        UserManager<ApplicationUser> userManager)
    {
        _subscriptionService = subscriptionService;
        _userManager = userManager;
    }

    [HttpGet("api/my-subscriptions")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [SwaggerOperation(
        Summary = "Lists the current user's subscriptions",
        Description = "Lists the Maxio subscriptions that belong to the authenticated user",
        OperationId = "subscriptions.mySubscriptions",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<GetMySubscriptionsResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user is null)
        {
            return Unauthorized();
        }

        var response = new GetMySubscriptionsResponse(Guid.NewGuid());
        var subscriptions = await _subscriptionService.GetSubscriptionsForUserAsync(
            MaxioUserInfoFactory.FromUser(user), cancellationToken);

        response.Subscriptions = subscriptions
            .Select(s => SubscriptionDtoMapper.ToDto(s, created: false))
            .ToList();

        return Ok(response);
    }

    private async Task<ApplicationUser?> GetCurrentUserAsync(CancellationToken cancellationToken)
    {
        var username = User.Claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.Name)?.Value;
        if (string.IsNullOrEmpty(username))
        {
            return null;
        }
        return await _userManager.FindByNameAsync(username);
    }
}
