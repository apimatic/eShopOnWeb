using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the signed-in shopper to a plan. Idempotent: subscribing to a plan the shopper
/// already holds returns the existing subscription.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class SubscribeEndpoint : EndpointBaseAsync
    .WithRequest<SubscribeRequest>
    .WithActionResult<SubscribeResponse>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ISubscriptionBillingService _subscriptionBillingService;

    public SubscribeEndpoint(
        UserManager<ApplicationUser> userManager,
        ISubscriptionBillingService subscriptionBillingService)
    {
        _userManager = userManager;
        _subscriptionBillingService = subscriptionBillingService;
    }

    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Subscribes the signed-in user to a plan",
        Description = "Ensures a Maxio customer exists for the user and subscribes them to the requested plan. Idempotent for repeat calls on the same plan.",
        OperationId = "subscriptions.subscribe",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<SubscribeResponse>> HandleAsync(
        SubscribeRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await CurrentUserResolver.GetCurrentUserAsync(User, _userManager);
        if (user is null)
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return BadRequest("A planHandle is required. Use GET /api/subscription-plans to list available plans.");
        }

        var subscriber = new SubscriberProfile
        {
            Reference = user.Id,
            Email = user.Email ?? user.UserName ?? string.Empty
        };

        var purchase = await _subscriptionBillingService.SubscribeAsync(subscriber, request.PlanHandle, cancellationToken);

        var response = new SubscribeResponse(request.CorrelationId())
        {
            Subscription = SubscriptionMapper.ToDto(purchase)
        };

        return purchase.AlreadySubscribed
            ? Ok(response)
            : StatusCode(StatusCodes.Status201Created, response);
    }
}
