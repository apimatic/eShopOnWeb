using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[Authorize]
public class GetUserSubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<ListUserSubscriptionsResponse>
{
    private readonly MaxioSubscriptionService _subscriptionService;
    private readonly UserManager<ApplicationUser> _userManager;

    public GetUserSubscriptionsEndpoint(
        MaxioSubscriptionService subscriptionService,
        UserManager<ApplicationUser> userManager)
    {
        _subscriptionService = subscriptionService;
        _userManager = userManager;
    }

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "Get user's subscriptions",
        Description = "Returns all subscriptions for the authenticated user",
        OperationId = "subscription.list",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<ListUserSubscriptionsResponse>> HandleAsync(CancellationToken ct = default)
    {
        var response = new ListUserSubscriptionsResponse();

        try
        {
            var userName = User.FindFirst(ClaimTypes.Name)?.Value;
            if (string.IsNullOrEmpty(userName))
            {
                return Unauthorized();
            }

            var user = await _userManager.FindByNameAsync(userName);
            if (user == null)
            {
                return Unauthorized();
            }

            var (customerId, _) = await _subscriptionService.EnsureMaxioCustomerAsync(
                userId: user.Id,
                email: user.Email ?? userName,
                firstName: ExtractFirstName(user.Email ?? userName),
                lastName: "Account",
                ct: ct);

            var subscriptions = await _subscriptionService.GetCustomerSubscriptionsAsync(
                customerId: customerId,
                ct: ct);

            response.Subscriptions.AddRange(subscriptions);
            return Ok(response);
        }
        catch
        {
            return StatusCode(500);
        }
    }

    private string ExtractFirstName(string email)
    {
        var parts = email.Split('@');
        if (parts.Length > 0 && parts[0].Length > 0)
        {
            return char.ToUpper(parts[0][0]) + (parts[0].Length > 1 ? parts[0].Substring(1) : "");
        }
        return "User";
    }
}
