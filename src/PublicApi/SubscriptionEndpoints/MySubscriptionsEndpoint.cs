using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<MySubscriptionsResponse>
{
    private readonly ISubscriptionService _subscriptionService;

    public MySubscriptionsEndpoint(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "Get user's subscriptions",
        Description = "Returns the list of active subscriptions for the authenticated user",
        OperationId = "subscriptions.list",
        Tags = new[] { "Subscriptions" }
    )]
    [Authorize]
    public override async Task<ActionResult<MySubscriptionsResponse>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        var response = new MySubscriptionsResponse { CorrelationId = Guid.NewGuid().ToString() };

        var userId = User.FindFirst(ClaimTypes.Name)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return BadRequest(new MySubscriptionsResponse
            {
                CorrelationId = response.CorrelationId,
                Success = false,
                Message = "User not authenticated",
                Subscriptions = new List<UserSubscriptionDto>()
            });
        }

        var subscriptions = await _subscriptionService.GetUserSubscriptions(userId);
        response.Subscriptions = new List<UserSubscriptionDto>();

        if (subscriptions != null)
        {
            foreach (var sub in subscriptions)
            {
                response.Subscriptions.Add(new UserSubscriptionDto
                {
                    Id = sub.Id,
                    State = sub.State,
                    PlanName = sub.Product?.Name,
                    PriceInCents = sub.Product?.PriceInCents ?? 0,
                    ActivatedAt = sub.ActivatedAt,
                    CurrentPeriodStartedAt = sub.CurrentPeriodStartedAt,
                    CurrentPeriodEndsAt = sub.CurrentPeriodEndsAt,
                    NextBillingDate = sub.NextAssessmentAt,
                    BalanceInCents = sub.BalanceInCents
                });
            }
        }

        response.Success = true;
        return Ok(response);
    }
}

public class MySubscriptionsResponse
{
    public string CorrelationId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? Message { get; set; }
    public List<UserSubscriptionDto> Subscriptions { get; set; } = new();
}

public class UserSubscriptionDto
{
    public int Id { get; set; }
    public string? State { get; set; }
    public string? PlanName { get; set; }
    public long PriceInCents { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime? CurrentPeriodStartedAt { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime? NextBillingDate { get; set; }
    public long BalanceInCents { get; set; }
}
