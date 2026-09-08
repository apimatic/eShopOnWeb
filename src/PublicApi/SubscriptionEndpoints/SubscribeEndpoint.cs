using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the signed-in user to a plan. Idempotent: subscribing to a plan the user already holds
/// returns the existing subscription.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class SubscribeEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<CreateSubscriptionResponse>
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly UserManager<ApplicationUser> _userManager;

    public SubscribeEndpoint(ISubscriptionService subscriptionService, UserManager<ApplicationUser> userManager)
    {
        _subscriptionService = subscriptionService;
        _userManager = userManager;
    }

    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Subscribes the signed-in user to a plan",
        Description = "Ensures the signed-in user has a billing customer and subscribes them to the requested plan. " +
                      "Repeated calls for the same plan return the existing subscription.",
        OperationId = "subscriptions.subscribe",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return BadRequest(new { message = "planHandle is required." });
        }

        var userName = User.Identity?.Name;
        if (string.IsNullOrEmpty(userName))
        {
            return Unauthorized();
        }

        var user = await _userManager.FindByNameAsync(userName);
        if (user is null)
        {
            return Unauthorized();
        }

        var result = await _subscriptionService.SubscribeAsync(new SubscriptionSignup
        {
            UserId = user.Id,
            Email = string.IsNullOrWhiteSpace(user.Email) ? userName : user.Email,
            PlanHandle = request.PlanHandle.Trim()
        }, cancellationToken);

        var response = new CreateSubscriptionResponse
        {
            Created = result.Created,
            Subscription = SubscriptionDtoFactory.ToDto(result.Subscription)
        };

        if (result.Created)
        {
            return StatusCode(StatusCodes.Status201Created, response);
        }

        return response;
    }
}
