using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a plan. Idempotent: repeating the same
/// signup returns the existing subscription and never creates a second customer
/// or subscription in Maxio.
/// </summary>
[Authorize(AuthenticationSchemes = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme)]
public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<CreateSubscriptionResponse>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IMaxioSubscriptionService _subscriptionService;

    public CreateSubscriptionEndpoint(UserManager<ApplicationUser> userManager,
        IMaxioSubscriptionService subscriptionService)
    {
        _userManager = userManager;
        _subscriptionService = subscriptionService;
    }

    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Subscribes the authenticated user to a plan",
        Description = "Ensures a Maxio customer exists for the user and enrolls them in the plan; idempotent on repeat calls",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.PlanHandle))
            return BadRequest(new { message = "planHandle is required." });

        var userName = User.Identity?.Name;
        if (string.IsNullOrEmpty(userName)) return Unauthorized();

        var user = await _userManager.FindByNameAsync(userName);
        if (user is null) return Unauthorized();

        var plan = await _subscriptionService.GetPlanAsync(request.PlanHandle.Trim(), cancellationToken);
        if (plan is null)
            return NotFound(new { message = $"Subscription plan '{request.PlanHandle}' was not found." });

        if (plan.RequireCreditCard)
            return UnprocessableEntity(new { message = $"Plan '{plan.Handle}' requires a payment method, which this flow does not collect." });

        var customer = await _subscriptionService.EnsureCustomerAsync(userName, user.Email ?? userName, cancellationToken);
        var (subscription, created) = await _subscriptionService.SubscribeAsync(customer, plan, cancellationToken);

        var response = new CreateSubscriptionResponse
        {
            AlreadySubscribed = !created,
            Subscription = MapSubscription(subscription)
        };

        return Ok(response);
    }

    internal static SubscriptionDto MapSubscription(MaxioSubscription subscription) =>
        new SubscriptionDto
        {
            Id = subscription.Id,
            State = subscription.State ?? string.Empty,
            PlanHandle = subscription.Product?.Handle,
            PlanName = subscription.Product?.Name,
            Price = subscription.Product?.PriceInCents is long productCents
                ? productCents / 100m
                : subscription.ProductPriceInCents / 100m,
            NextBillingDate = subscription.CurrentPeriodEndsAt,
            ActivatedAt = subscription.ActivatedAt,
            CreatedAt = subscription.CreatedAt,
            CanceledAt = subscription.CanceledAt,
            CustomerId = subscription.Customer?.Id ?? 0
        };
}
