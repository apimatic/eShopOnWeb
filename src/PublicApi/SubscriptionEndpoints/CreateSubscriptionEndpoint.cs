using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to. When omitted, the configured default
    /// plan is used (or the family's only active plan).
    /// </summary>
    public string? PlanHandle { get; set; }
}

/// <summary>
/// Subscribes the authenticated user to a plan in Maxio Advanced Billing.
/// Idempotent: ensures a Maxio customer exists for the user (keyed by the user
/// id as Maxio reference) and never creates a duplicate subscription for the
/// same plan.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<CreateSubscriptionResponse>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ISubscriptionService _subscriptionService;

    public CreateSubscriptionEndpoint(UserManager<ApplicationUser> userManager,
        ISubscriptionService subscriptionService)
    {
        _userManager = userManager;
        _subscriptionService = subscriptionService;
    }

    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Subscribes the authenticated user to a plan",
        Description = "Ensures a Maxio customer exists for the user and enrolls them in the requested subscription plan",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CreateSubscriptionRequest? request,
        CancellationToken cancellationToken = default)
    {
        request ??= new CreateSubscriptionRequest();

        var username = User.Identity?.Name;
        if (string.IsNullOrEmpty(username))
        {
            return Unauthorized();
        }

        var user = await _userManager.FindByNameAsync(username);
        if (user == null)
        {
            return Unauthorized();
        }

        try
        {
            var subscription = await _subscriptionService.SubscribeAsync(
                user.Id,
                user.Email ?? username,
                username,
                "(eShopOnWeb)",
                request.PlanHandle,
                cancellationToken);

            return Ok(new CreateSubscriptionResponse(request.CorrelationId())
            {
                Subscriptions = { subscription }
            });
        }
        catch (MaxioPlanNotFoundException ex)
        {
            return Problem(
                title: "Unknown subscription plan",
                detail: $"{ex.Message} Available plans: {string.Join(", ", ex.AvailablePlanHandles)}.",
                statusCode: 400);
        }
        catch (MaxioConfigurationException ex)
        {
            return Problem(title: "Subscription request cannot be fulfilled", detail: ex.Message, statusCode: 503);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 409)
        {
            // Maxio duplicate-prevention: the same uniqueness_token was already
            // submitted; the first request owns the subscription.
            return Problem(
                title: "Duplicate subscription submission",
                detail: "This subscription request was already submitted to Maxio and is being processed.",
                statusCode: 409);
        }
        catch (MaxioApiException ex)
        {
            return Problem(
                title: "Maxio subscription request failed",
                detail: string.Join(" ", ex.Errors.Prepend(ex.Message)),
                statusCode: 502);
        }
    }
}
