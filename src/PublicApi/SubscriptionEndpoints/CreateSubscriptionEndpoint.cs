using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.Infrastructure.Services.Maxio;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[Authorize]
public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<CreateSubscriptionResponse>
{
    private readonly MaxioSubscriptionService _subscriptionService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(MaxioSubscriptionService subscriptionService, IHttpContextAccessor httpContextAccessor)
    {
        _subscriptionService = subscriptionService;
        _httpContextAccessor = httpContextAccessor;
    }

    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Create a subscription",
        Description = "Creates a subscription for the authenticated user",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(
        CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = new CreateSubscriptionResponse();

        try
        {
            var userIdClaim = _httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier);
            var userEmailClaim = _httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.Email);
            var userNameClaim = _httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.Name);

            if (userIdClaim == null || string.IsNullOrEmpty(userIdClaim.Value))
            {
                return Unauthorized(new { message = "User ID not found in token" });
            }

            var userId = userIdClaim.Value;
            var userEmail = userEmailClaim?.Value ?? $"user-{userId}@eshop.local";
            var userName = userNameClaim?.Value ?? "User";
            var nameParts = userName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            var firstName = nameParts.Length > 0 ? nameParts[0] : "User";
            var lastName = nameParts.Length > 1 ? nameParts[1] : "";

            // Get or create customer
            var customer = await _subscriptionService.GetOrCreateCustomerAsync(userId, firstName, lastName, userEmail);
            if (customer == null)
            {
                response.Success = false;
                response.Message = "Failed to create or retrieve customer";
                return StatusCode(500, response);
            }

            // Create subscription
            var subscription = await _subscriptionService.CreateSubscriptionAsync(customer.Id, request.PlanHandle);
            if (subscription == null)
            {
                response.Success = false;
                response.Message = "Failed to create subscription";
                return StatusCode(500, response);
            }

            response.Success = true;
            response.SubscriptionId = subscription.Id;
            response.CustomerId = subscription.CustomerId;
            response.State = subscription.State;
            response.CreatedAt = subscription.CreatedAt;
            response.NextBillingAt = subscription.NextBillingAt;

            if (subscription.Product != null)
            {
                response.PlanName = subscription.Product.Name;
                response.PriceInCents = subscription.Product.PriceInCents;
            }

            return CreatedAtAction(nameof(CreateSubscriptionEndpoint), new { id = subscription.Id }, response);
        }
        catch (Exception ex)
        {
            response.Success = false;
            response.Message = $"Error creating subscription: {ex.Message}";
            return StatusCode(500, response);
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
    public string? Message { get; set; }
    public int SubscriptionId { get; set; }
    public int CustomerId { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? NextBillingAt { get; set; }
    public string? PlanName { get; set; }
    public long PriceInCents { get; set; }

    public decimal Price => PriceInCents / 100m;
}
