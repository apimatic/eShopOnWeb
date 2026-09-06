using System;
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
public class SubscriptionCreateEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<CreateSubscriptionResponse>
{
    private readonly IMaxioBillingService _billingService;

    public SubscriptionCreateEndpoint(IMaxioBillingService billingService)
    {
        _billingService = billingService;
    }

    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Create a subscription",
        Description = "Create a new subscription for the authenticated user",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" }
    )]
    [ProducesResponseType(typeof(CreateSubscriptionResponse), 201)]
    [ProducesResponseType(400)]
    [ProducesResponseType(500)]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(
        CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return BadRequest(new { error = "Plan handle is required" });
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized(new { error = "User identification failed" });
        }

        try
        {
            var subscription = await _billingService.CreateSubscriptionAsync(userId, request.PlanHandle);

            if (subscription == null)
            {
                return StatusCode(500, new { error = "Failed to create subscription" });
            }

            var response = new CreateSubscriptionResponse
            {
                Success = true,
                SubscriptionId = subscription.Id,
                State = subscription.State,
                PlanHandle = subscription.ProductHandle,
                CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
                NextAssessmentAt = subscription.NextAssessmentAt,
                ActivatedAt = subscription.ActivatedAt,
                Message = "Subscription created successfully"
            };

            return CreatedAtAction(nameof(SubscriptionListEndpoint), null, response);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Failed to create subscription", message = ex.Message });
        }
    }
}

public class CreateSubscriptionRequest
{
    public string PlanHandle { get; set; } = string.Empty;
}

public class CreateSubscriptionResponse
{
    public bool Success { get; set; }
    public int SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public string? PlanHandle { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime? NextAssessmentAt { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public string Message { get; set; } = string.Empty;
}
