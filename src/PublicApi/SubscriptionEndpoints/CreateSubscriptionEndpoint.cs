using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Subscriptions;
using Microsoft.eShopWeb.PublicApi.Middleware;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a plan. Ensures an idempotent Maxio
/// customer exists for the user, enrolls them, and returns plan, price,
/// state and next-billing date.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<CreateSubscriptionResponse>
{
    private readonly IMaxioSubscriptionService _subscriptionService;
    private readonly UserManager<Microsoft.eShopWeb.Infrastructure.Identity.ApplicationUser> _userManager;

    public CreateSubscriptionEndpoint(
        IMaxioSubscriptionService subscriptionService,
        UserManager<Microsoft.eShopWeb.Infrastructure.Identity.ApplicationUser> userManager)
    {
        _subscriptionService = subscriptionService;
        _userManager = userManager;
    }

    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Subscribes the authenticated user to a plan",
        Description = "Ensures a Maxio customer exists for the user and creates the subscription in Maxio",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByNameAsync(User.Identity?.Name ?? string.Empty);
        if (user is null)
        {
            return Unauthorized();
        }

        var response = new CreateSubscriptionResponse(request.CorrelationId());

        try
        {
            var subscription = await _subscriptionService.SubscribeAsync(user, request.PlanHandle, cancellationToken);
            response.Subscription = ToDto(subscription);
            return Ok(response);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 404)
        {
            return NotFound(new { errors = ex.Errors });
        }
        catch (InvalidOperationException ex)
        {
            // Maxio integration not configured
            return StatusCode(503, new { errors = new[] { ex.Message } });
        }
    }

    private static SubscriptionDto ToDto(SubscriptionInfo subscription)
        => new()
        {
            SubscriptionId = subscription.MaxioSubscriptionId,
            PlanHandle = subscription.PlanHandle,
            PlanName = subscription.PlanName,
            PriceInCents = subscription.PriceInCents,
            Price = SubscriptionPlansListEndpoint.FormatPrice(subscription.PriceInCents),
            State = subscription.State,
            NextBillingAt = subscription.NextBillingAt,
            AlreadySubscribed = subscription.AlreadySubscribed
        };
}
