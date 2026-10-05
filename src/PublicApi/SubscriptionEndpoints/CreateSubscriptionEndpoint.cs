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
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a plan (Maxio product). Idempotent:
/// a repeated call returns the user's existing active subscription for the plan.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<CreateSubscriptionResponse>
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateSubscriptionEndpoint(ISubscriptionService subscriptionService, UserManager<ApplicationUser> userManager)
    {
        _subscriptionService = subscriptionService;
        _userManager = userManager;
    }

    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Subscribes the user to a plan",
        Description = "Ensures a Maxio customer exists for the user, then subscribes them to the plan. Idempotent: returns the existing active subscription when the user is already subscribed.",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request?.PlanHandle))
        {
            return Problem(title: "Plan handle is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        // The JWT carries the username as ClaimTypes.Name.
        var username = User.Identity?.Name;
        var appUser = string.IsNullOrEmpty(username) ? null : await _userManager.FindByNameAsync(username);
        if (appUser is null || string.IsNullOrEmpty(appUser.Id) || string.IsNullOrEmpty(appUser.Email))
        {
            return Problem(title: "The authenticated user could not be resolved.", statusCode: StatusCodes.Status401Unauthorized);
        }

        bool alreadySubscribed;
        try
        {
            var (subscription, existing, created) =
                await _subscriptionService.SubscribeAsync(appUser.Id, appUser.UserName ?? appUser.Email, appUser.Email, request.PlanHandle, cancellationToken);
            alreadySubscribed = existing;
            response.Subscription = subscription;
            response.AlreadySubscribed = alreadySubscribed;
            response.CustomerCreated = created;
        }
        catch (UnknownPlanException ex)
        {
            return Problem(title: ex.Message, statusCode: StatusCodes.Status404NotFound);
        }

        return alreadySubscribed ? Ok(response) : Created("api/my-subscriptions", response);
    }
}