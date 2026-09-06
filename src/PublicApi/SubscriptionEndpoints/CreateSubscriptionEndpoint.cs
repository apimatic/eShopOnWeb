using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<CreateSubscriptionResponse>
{
    private readonly ISubscriptionService _subscriptionService;

    public CreateSubscriptionEndpoint(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Create a new subscription",
        Description = "Subscribes the user to a plan",
        OperationId = "subscriptions.create",
        Tags = new[] { "Subscriptions" }
    )]
    [Authorize]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(
        CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = new CreateSubscriptionResponse { CorrelationId = Guid.NewGuid().ToString() };

        var userId = User.FindFirst(ClaimTypes.Name)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return BadRequest(new CreateSubscriptionResponse
            {
                CorrelationId = response.CorrelationId,
                Success = false,
                Message = "User not authenticated"
            });
        }

        if (string.IsNullOrEmpty(request.ProductHandle))
        {
            return BadRequest(new CreateSubscriptionResponse
            {
                CorrelationId = response.CorrelationId,
                Success = false,
                Message = "Product handle is required"
            });
        }

        var email = userId;
        var firstName = userId.Split('@')[0] ?? "Customer";
        var lastName = string.Empty;

        if (string.IsNullOrEmpty(email))
        {
            return BadRequest(new CreateSubscriptionResponse
            {
                CorrelationId = response.CorrelationId,
                Success = false,
                Message = "User email is required for subscription"
            });
        }

        var customerExists = await _subscriptionService.EnsureCustomerExists(userId, firstName, lastName, email);
        if (!customerExists)
        {
            return StatusCode(500, new CreateSubscriptionResponse
            {
                CorrelationId = response.CorrelationId,
                Success = false,
                Message = "Failed to create Maxio customer"
            });
        }

        var subscription = await _subscriptionService.CreateSubscription(userId, request.ProductHandle);
        if (subscription == null)
        {
            return StatusCode(500, new CreateSubscriptionResponse
            {
                CorrelationId = response.CorrelationId,
                Success = false,
                Message = "Failed to create subscription"
            });
        }

        response.Success = true;
        response.SubscriptionId = subscription.Id;
        response.State = subscription.State;
        response.NextBillingDate = subscription.NextAssessmentAt;

        if (subscription.Product != null)
        {
            response.PlanName = subscription.Product.Name;
            response.PriceInCents = subscription.Product.PriceInCents;
        }

        return Created($"api/subscriptions/{subscription.Id}", response);
    }
}

public class CreateSubscriptionRequest
{
    public string? ProductHandle { get; set; }
}

public class CreateSubscriptionResponse
{
    public string CorrelationId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? Message { get; set; }
    public int SubscriptionId { get; set; }
    public string? State { get; set; }
    public string? PlanName { get; set; }
    public long PriceInCents { get; set; }
    public DateTime? NextBillingDate { get; set; }
}
