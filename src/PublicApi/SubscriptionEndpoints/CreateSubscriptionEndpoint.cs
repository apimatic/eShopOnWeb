using System;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a plan. Idempotent: a repeated or
/// double-clicked subscribe for the same user and plan never creates a second
/// Maxio customer or subscription.
/// </summary>
[Authorize]
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
        Summary = "Subscribes the current user to a plan",
        Description = "Enrolls the authenticated user in the plan identified by its product handle",
        OperationId = "subscription.create",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(
        CreateSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request?.ProductHandle))
            return BadRequest("ProductHandle is required.");

        var user = await _userManager.FindByNameAsync(User.Identity!.Name!);
        if (user is null)
            return Unauthorized();

        var summary = await _subscriptionService.SubscribeAsync(
            user.Id, user.UserName ?? user.Email ?? "", user.Email ?? user.UserName ?? "",
            request.ProductHandle, cancellationToken);

        if (summary is null)
            return NotFound($"Unknown subscription plan '{request.ProductHandle}'.");

        response.Subscription = SubscriptionPlanListEndpoint.ToDto(summary);
        return response;
    }
}
