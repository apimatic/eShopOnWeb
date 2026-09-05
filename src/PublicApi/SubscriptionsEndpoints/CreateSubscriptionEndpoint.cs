using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi.Services;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionsEndpoints;

public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<CreateSubscriptionResponse>
{
    private readonly IMaxioService _maxioService;

    public CreateSubscriptionEndpoint(IMaxioService maxioService)
    {
        _maxioService = maxioService;
    }

    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Create a subscription",
        Description = "Creates a new subscription for the authenticated user",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionsEndpoints" })
    ]
    [Authorize(AuthenticationSchemes = "Bearer")]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(
        CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        try
        {
            var userName = User?.FindFirst(ClaimTypes.Name)?.Value;
            if (string.IsNullOrEmpty(userName))
            {
                return Unauthorized();
            }

            var email = User?.FindFirst(ClaimTypes.Email)?.Value ?? userName;

            if (string.IsNullOrEmpty(request.ProductHandle))
            {
                return BadRequest(new { error = "ProductHandle is required" });
            }

            var customer = await _maxioService.GetOrCreateCustomerAsync(userName, email);
            if (customer?.Id == null)
            {
                return BadRequest(new { error = "Failed to create or retrieve customer" });
            }

            var subscription = await _maxioService.CreateSubscriptionAsync((int)customer.Id, request.ProductHandle);

            response.Subscription = new SubscriptionDto
            {
                Id = subscription.Id,
                State = subscription.State?.ToString(),
                ProductName = subscription.Product?.Name,
                ProductHandle = subscription.Product?.Handle,
                CurrentBillingAmountInCents = subscription.CurrentBillingAmountInCents,
                CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
                CreatedAt = subscription.CreatedAt,
                ActivatedAt = subscription.ActivatedAt,
                UpdatedAt = subscription.UpdatedAt
            };

            return Created($"/api/subscriptions/{subscription.Id}", response);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = "Failed to create subscription", details = ex.Message });
        }
    }
}
