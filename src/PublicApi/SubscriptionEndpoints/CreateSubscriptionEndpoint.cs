using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.Infrastructure.Services;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[Authorize]
public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<CreateSubscriptionResponse>
{
    private readonly IMaxioSubscriptionService _subscriptionService;

    public CreateSubscriptionEndpoint(IMaxioSubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Create a new subscription",
        Description = "Subscribe the current user to a plan",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(
        CreateSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        try
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(new { message = "User ID not found in token" });
            }

            var subscription = await _subscriptionService.SubscribeAsync(
                userId, request.PlanHandle, cancellationToken);

            response.Subscription = new SubscriptionStateDto
            {
                Id = subscription.Id,
                CustomerId = subscription.CustomerId,
                ProductId = subscription.ProductId,
                State = subscription.State,
                ActivatedAt = subscription.ActivatedAt,
                CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
                CreatedAt = subscription.CreatedAt
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message, correlationId = response.CorrelationId() });
        }
    }
}
