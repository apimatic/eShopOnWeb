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
public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<CreateSubscriptionResponse>
{
    private readonly MaxioSubscriptionService _subscriptionService;
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateSubscriptionEndpoint(
        MaxioSubscriptionService subscriptionService,
        UserManager<ApplicationUser> userManager)
    {
        _subscriptionService = subscriptionService;
        _userManager = userManager;
    }

    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Subscribe user to a plan",
        Description = "Creates a new subscription for the authenticated user",
        OperationId = "subscription.create",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(
        CreateSubscriptionRequest request,
        CancellationToken ct = default)
    {
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

            var subscription = await _subscriptionService.CreateSubscriptionAsync(
                customerId: customerId,
                productHandle: request.ProductHandle,
                ct: ct);

            var response = new CreateSubscriptionResponse { Subscription = subscription };
            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            var response = new CreateSubscriptionResponse { ErrorMessage = ex.Message };
            return BadRequest(response);
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
