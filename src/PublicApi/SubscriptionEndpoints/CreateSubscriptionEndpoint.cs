using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a plan. Idempotent per user + plan.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<CreateSubscriptionResponse>
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateSubscriptionEndpoint(ISubscriptionService subscriptionService,
        UserManager<ApplicationUser> userManager)
    {
        _subscriptionService = subscriptionService;
        _userManager = userManager;
    }

    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Subscribes to a plan",
        Description = "Subscribes the authenticated user to the given plan. Repeated calls for the same plan are idempotent.",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(
        CreateSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(cancellationToken);
        if (user is null)
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request?.PlanHandle))
        {
            return BadRequest(new { error = "planHandle is required." });
        }

        try
        {
            var subscription = await _subscriptionService.SubscribeAsync(user.Id, user.Email ?? string.Empty,
                user.UserName ?? string.Empty, request.PlanHandle, cancellationToken);

            return Ok(new CreateSubscriptionResponse
            {
                CorrelationId = Guid.NewGuid().ToString(),
                Subscription = Map(subscription)
            });
        }
        catch (SubscriptionPlanNotFoundException)
        {
            return NotFound(new { error = $"Plan '{request.PlanHandle}' was not found." });
        }
    }

    private async Task<ApplicationUser?> ResolveUserAsync(CancellationToken cancellationToken)
    {
        var name = User?.Identity?.Name;
        return string.IsNullOrEmpty(name) ? null : await _userManager.FindByNameAsync(name);
    }

    internal static SubscriptionDto Map(ApplicationCore.Entities.SubscriptionAggregate.UserSubscription s) =>
        new()
        {
            Id = s.Id,
            PlanHandle = s.PlanHandle,
            PlanName = s.PlanName,
            PriceInCents = s.PriceInCents,
            Price = $"${s.PriceInCents / 100m:0.00}",
            State = s.State,
            NextBillingAt = s.NextBillingAt,
            BillingSubscriptionId = s.BillingSubscriptionId
        };
}
