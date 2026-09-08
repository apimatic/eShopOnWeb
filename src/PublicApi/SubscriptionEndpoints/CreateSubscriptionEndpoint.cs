using System;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the signed-in shopper to a plan on Maxio Advanced Billing.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<CreateSubscriptionResponse>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly MaxioSubscriptionService _subscriptionService;

    public CreateSubscriptionEndpoint(
        UserManager<ApplicationUser> userManager,
        MaxioSubscriptionService subscriptionService)
    {
        _userManager = userManager;
        _subscriptionService = subscriptionService;
    }

    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Subscribes the current shopper to a plan",
        Description = "Ensures a Maxio Advanced Billing customer exists for the signed-in shopper (idempotent) and subscribes them to the requested plan. If the shopper already holds an open subscription to the plan, the existing subscription is returned instead of creating a duplicate.",
        OperationId = "subscriptions.subscribe",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(
        CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return BadRequest("A planHandle is required to subscribe to a plan.");
        }

        var shopper = await CurrentShopperAsync(cancellationToken);
        if (shopper is null)
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(shopper.Email))
        {
            return Unauthorized();
        }

        try
        {
            var (subscription, created) = await _subscriptionService.SubscribeAsync(
                shopper.Email, request.PlanHandle, cancellationToken);

            response.Subscription = SubscriptionMapping.ToSubscriptionDto(subscription);
            response.Created = created;

            return created
                ? Created($"api/subscriptions/{subscription.Id}", response)
                : Ok(response);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    private async Task<ApplicationUser?> CurrentShopperAsync(CancellationToken cancellationToken = default)
    {
        var name = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return await _userManager.FindByNameAsync(name);
    }
}
