using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Services;
using Swashbuckle.AspNetCore.Annotations;
using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[Authorize]
public class SubscriptionsCreateEndpoint : EndpointBaseAsync
    .WithRequest<SubscriptionsCreateEndpoint.CreateSubscriptionRequest>
    .WithActionResult<SubscriptionsCreateEndpoint.Response>
{
    private readonly MaxioSubscriptionService _subscriptionService;
    private readonly UserManager<ApplicationUser> _userManager;

    public SubscriptionsCreateEndpoint(
        MaxioSubscriptionService subscriptionService,
        UserManager<ApplicationUser> userManager)
    {
        _subscriptionService = subscriptionService;
        _userManager = userManager;
    }

    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Create a subscription",
        Description = "Subscribe the authenticated user to a subscription plan",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionsEndpoints" })]
    public override async Task<ActionResult<Response>> HandleAsync(
        CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Get the authenticated user
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized(new { error = "User not authenticated" });
            }

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return NotFound(new { error = "User not found" });
            }

            if (string.IsNullOrWhiteSpace(request.ProductHandle))
            {
                return BadRequest(new { error = "ProductHandle is required" });
            }

            var subscription = await _subscriptionService.CreateSubscriptionAsync(
                userId,
                user.Email ?? string.Empty,
                user.UserName ?? string.Empty,
                user.UserName ?? string.Empty,
                request.ProductHandle,
                cancellationToken);

            return Ok(new Response
            {
                SubscriptionId = subscription.Id,
                State = subscription.State,
                ProductHandle = subscription.ProductHandle,
                CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
                NextAssessmentAt = subscription.NextAssessmentAt,
                ProductPriceInCents = subscription.ProductPriceInCents
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    public sealed record CreateSubscriptionRequest
    {
        public required string ProductHandle { get; set; }
    }

    public sealed record Response
    {
        public int SubscriptionId { get; set; }
        public required string State { get; set; }
        public required string ProductHandle { get; set; }
        public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
        public DateTimeOffset? NextAssessmentAt { get; set; }
        public long ProductPriceInCents { get; set; }
    }
}
