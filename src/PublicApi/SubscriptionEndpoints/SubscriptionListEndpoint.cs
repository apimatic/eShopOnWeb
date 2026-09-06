using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[Authorize]
public class SubscriptionListEndpoint : EndpointBaseAsync.WithoutRequest.WithActionResult<SubscriptionListResponse>
{
    private readonly IMaxioBillingService _billingService;

    public SubscriptionListEndpoint(IMaxioBillingService billingService)
    {
        _billingService = billingService;
    }

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "List user subscriptions",
        Description = "List all subscriptions for the authenticated user",
        OperationId = "subscriptions.list-my",
        Tags = new[] { "SubscriptionEndpoints" }
    )]
    [ProducesResponseType(typeof(SubscriptionListResponse), 200)]
    [ProducesResponseType(500)]
    public override async Task<ActionResult<SubscriptionListResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized(new { error = "User identification failed" });
        }

        var response = new SubscriptionListResponse();

        try
        {
            var subscriptions = await _billingService.GetCustomerSubscriptionsAsync(userId);

            response.Subscriptions = subscriptions.Select(s => new SubscriptionResponse
            {
                Id = s.Id,
                State = s.State,
                PlanHandle = s.ProductHandle ?? s.Product?.Handle,
                PlanName = s.Product?.Name,
                Price = s.Product != null ? s.Product.PriceInCents / 100m : 0,
                Interval = s.Product?.Interval ?? 1,
                IntervalUnit = s.Product?.IntervalUnit ?? "month",
                CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
                NextAssessmentAt = s.NextAssessmentAt,
                ActivatedAt = s.ActivatedAt,
                CreatedAt = s.CreatedAt
            }).ToList();

            response.Success = true;
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Failed to retrieve subscriptions", message = ex.Message });
        }

        return Ok(response);
    }
}

public class SubscriptionListResponse
{
    public List<SubscriptionResponse> Subscriptions { get; set; } = new();
    public bool Success { get; set; }
}

public class SubscriptionResponse
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    public decimal Price { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime? NextAssessmentAt { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime? CreatedAt { get; set; }
}
