using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Subscription;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a plan. Idempotent: if the user already holds a live
/// subscription to the plan, it is returned again (HTTP 200) rather than duplicated (HTTP 201).
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<CreateSubscriptionResponse>
{
    private readonly ISubscriptionBillingService _billingService;
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateSubscriptionEndpoint(ISubscriptionBillingService billingService, UserManager<ApplicationUser> userManager)
    {
        _billingService = billingService;
        _userManager = userManager;
    }

    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Subscribe to a Plan",
        Description = "Ensures a billing customer exists for the authenticated user and enrolls them in the requested plan",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var username = HttpContext.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
        {
            return Unauthorized();
        }

        var user = await _userManager.FindByNameAsync(username);
        if (user is null)
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request?.PlanHandle))
        {
            return BadRequest(new { errors = new[] { "planHandle is required." } });
        }

        SubscriptionSummary subscription;
        try
        {
            subscription = await _billingService.SubscribeAsync(
                user.UserName!,
                user.Email ?? user.UserName!,
                request.PlanHandle,
                cancellationToken);
        }
        catch (MaxioPlanNotFoundException)
        {
            return NotFound(new { errors = new[] { $"Subscription plan '{request.PlanHandle}' was not found." } });
        }
        catch (MaxioApiException ex) when (ex.StatusCode >= 400 && ex.StatusCode < 500)
        {
            return StatusCode((int)HttpStatusCode.UnprocessableEntity,
                new { errors = ex.Errors });
        }

        var response = new CreateSubscriptionResponse
        {
            Subscription = new SubscriptionDto
            {
                Id = subscription.Id,
                State = subscription.State,
                PlanHandle = subscription.PlanHandle,
                PlanName = subscription.PlanName,
                PriceInCents = subscription.PriceInCents,
                Price = ListSubscriptionPlansEndpoint.FormatPrice(subscription.PriceInCents),
                NextBillingDate = subscription.NextBillingDate,
                ActivatedAt = subscription.ActivatedAt
            }
        };

        return subscription.ExistingReturned ? Ok(response) : Created($"api/my-subscriptions", response);
    }
}
