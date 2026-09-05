using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi.Services;
using Swashbuckle.AspNetCore.Annotations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[Authorize]
public class SubscriptionsListEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<SubscriptionsListEndpoint.Response>
{
    private readonly MaxioSubscriptionService _subscriptionService;

    public SubscriptionsListEndpoint(MaxioSubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "List user's subscriptions",
        Description = "Retrieve all subscriptions for the authenticated user",
        OperationId = "subscriptions.listMy",
        Tags = new[] { "SubscriptionsEndpoints" })]
    public override async Task<ActionResult<Response>> HandleAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Get the authenticated user
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized(new { error = "User not authenticated" });
            }

            var subscriptions = await _subscriptionService.ListCustomerSubscriptionsAsync(userId, cancellationToken);

            return Ok(new Response
            {
                Subscriptions = subscriptions.Select(s => new SubscriptionDto
                {
                    SubscriptionId = s.Id,
                    State = s.State,
                    ProductHandle = s.ProductHandle,
                    CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
                    NextAssessmentAt = s.NextAssessmentAt,
                    ProductPriceInCents = s.ProductPriceInCents
                }).ToList()
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    public sealed record Response
    {
        public required List<SubscriptionDto> Subscriptions { get; set; }
    }

    public sealed record SubscriptionDto
    {
        public int SubscriptionId { get; set; }
        public required string State { get; set; }
        public required string ProductHandle { get; set; }
        public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
        public DateTimeOffset? NextAssessmentAt { get; set; }
        public long ProductPriceInCents { get; set; }
    }
}
