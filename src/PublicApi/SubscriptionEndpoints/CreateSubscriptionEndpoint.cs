using System;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a plan. Idempotent: a repeat request for a plan
/// the user is already subscribed to returns the existing subscription.
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
        Summary = "Subscribe to a plan",
        Description = "Enrolls the authenticated user in the given subscription plan",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return BadRequest(new { message = "planHandle is required." });
        }

        var userName = User?.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName) || await _userManager.FindByNameAsync(userName) is null)
        {
            return Unauthorized();
        }

        try
        {
            var (subscription, created) = await _subscriptionService.SubscribeAsync(userName, request.PlanHandle.Trim(), cancellationToken);

            var response = new CreateSubscriptionResponse
            {
                Created = created,
                Subscription = ToDto(subscription)
            };

            return created
                ? Created(new Uri("/api/my-subscriptions", UriKind.Relative), response)
                : Ok(response);
        }
        catch (SubscriptionPlanNotFoundException)
        {
            return NotFound(new { message = $"No subscription plan with handle '{request.PlanHandle}' was found." });
        }
        catch (MaxioApiException ex)
        {
            return StatusCode(502, new { message = $"The billing system rejected the subscription: {ex.Message}" });
        }
    }

    internal static SubscriptionDto ToDto(ApplicationCore.Models.Subscriptions.SubscriptionDetails subscription)
    {
        return new SubscriptionDto
        {
            Id = subscription.Id,
            PlanId = subscription.PlanId,
            PlanHandle = subscription.PlanHandle,
            PlanName = subscription.PlanName,
            Price = subscription.Price,
            State = subscription.State,
            NextBillingDate = subscription.NextBillingDate,
            ActivatedAt = subscription.ActivatedAt,
            CreatedAt = subscription.CreatedAt,
            CustomerId = subscription.CustomerId
        };
    }
}
