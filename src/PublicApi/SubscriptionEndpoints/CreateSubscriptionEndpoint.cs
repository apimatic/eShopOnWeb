using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a plan in Maxio Advanced Billing.
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
        Summary = "Subscribes the user to a plan",
        Description = "Ensures a Maxio customer exists for the authenticated user, then subscribes them to the requested plan (idempotent)",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(cancellationToken);
        if (user == null)
        {
            return Unauthorized();
        }

        var result = await _subscriptionService.SubscribeAsync(
            user.Id, user.UserName ?? string.Empty, user.Email ?? string.Empty, request.PlanHandle, cancellationToken);

        var product = result.Product;
        var subscription = result.Subscription;

        var response = new CreateSubscriptionResponse()
        {
            SubscriptionId = subscription.Id,
            AlreadySubscribed = result.AlreadySubscribed,
            PlanHandle = product.Handle ?? string.Empty,
            PlanName = product.Name,
            PriceInCents = subscription.ProductPriceInCents ?? product.PriceInCents,
            Price = ListSubscriptionPlansEndpoint.FormatPrice(subscription.ProductPriceInCents ?? product.PriceInCents),
            Interval = product.Interval,
            IntervalUnit = product.IntervalUnit,
            State = subscription.State,
            NextBillingDate = subscription.CurrentPeriodEndsAt,
            CreatedAt = subscription.CreatedAt,
            MaxioCustomerId = result.Customer.Id,
        };

        return Ok(response);
    }

    private async Task<ApplicationUser?> ResolveUserAsync(CancellationToken cancellationToken)
    {
        var username = User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(username))
        {
            return null;
        }

        return await _userManager.FindByNameAsync(username);
    }
}