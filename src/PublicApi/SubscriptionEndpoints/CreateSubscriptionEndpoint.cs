using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using BlazorShared.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan.
/// Idempotent: when the user is already on the plan, the existing subscription is returned.
/// </summary>
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
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [SwaggerOperation(
        Summary = "Subscribes the current user to a plan",
        Description = "Ensures a Maxio customer exists for the caller (idempotent) and enrolls them on the plan. " +
                      "Returns 201 when a new subscription is created and 200 when the user is already subscribed to the plan.",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(
        CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return BadRequest(new ErrorDetails { StatusCode = 400, Message = "A plan handle is required (field 'planHandle')." });
        }

        var user = await ResolveCurrentUserAsync(cancellationToken);
        if (user == null)
        {
            return Unauthorized();
        }

        var result = await _subscriptionService.SubscribeAsync(user, request.PlanHandle.Trim(), cancellationToken);

        response.Subscription = result.Subscription.ToSubscriptionDto();
        response.Created = result.Created;

        return result.Created
            ? Created("api/subscriptions", response)
            : Ok(response);
    }

    private async Task<ApplicationUser?> ResolveCurrentUserAsync(CancellationToken cancellationToken)
    {
        var userName = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        return await _userManager.FindByNameAsync(userName);
    }
}
