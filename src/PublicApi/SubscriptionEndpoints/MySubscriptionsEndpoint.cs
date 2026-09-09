using System;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.Infrastructure.Subscriptions;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Returns the authenticated user's subscriptions, refreshed live from Maxio
/// when Maxio is reachable.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class MySubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<MySubscriptionsResponse>
{
    private readonly IMaxioSubscriptionService _subscriptionService;
    private readonly UserManager<Microsoft.eShopWeb.Infrastructure.Identity.ApplicationUser> _userManager;

    public MySubscriptionsEndpoint(
        IMaxioSubscriptionService subscriptionService,
        UserManager<Microsoft.eShopWeb.Infrastructure.Identity.ApplicationUser> userManager)
    {
        _subscriptionService = subscriptionService;
        _userManager = userManager;
    }

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "Lists the authenticated user's subscriptions",
        Description = "Lists the user's subscriptions with live state from Maxio when available",
        OperationId = "subscriptions.mySubscriptions",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<MySubscriptionsResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByNameAsync(User.Identity?.Name ?? string.Empty);
        if (user is null)
        {
            return Unauthorized();
        }

        var response = new MySubscriptionsResponse(Guid.NewGuid());

        try
        {
            var subscriptions = await _subscriptionService.GetMySubscriptionsAsync(user, cancellationToken);
            foreach (var subscription in subscriptions)
            {
                response.Subscriptions.Add(new MySubscriptionDto
                {
                    SubscriptionId = subscription.MaxioSubscriptionId,
                    PlanHandle = subscription.PlanHandle,
                    PlanName = subscription.PlanName,
                    PriceInCents = subscription.PriceInCents,
                    Price = SubscriptionPlansListEndpoint.FormatPrice(subscription.PriceInCents),
                    State = subscription.State,
                    NextBillingAt = subscription.NextBillingAt,
                    CreatedAt = subscription.CreatedAtUtc,
                    Live = subscription.Live
                });
            }

            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            // Maxio integration not configured
            return StatusCode(503, new { errors = new[] { ex.Message } });
        }
    }
}
