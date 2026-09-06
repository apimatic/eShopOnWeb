using System;
using System.Linq;
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
public class GetMySubscriptionsEndpoint : EndpointBaseAsync
    .WithRequest<Unit>
    .WithActionResult<ListMySubscriptionsResponse>
{
    private readonly IMaxioSubscriptionService _subscriptionService;

    public GetMySubscriptionsEndpoint(IMaxioSubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "Get user's subscriptions",
        Description = "Returns all active subscriptions for the current user",
        OperationId = "subscriptions.getUser",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<ListMySubscriptionsResponse>> HandleAsync(
        Unit request, CancellationToken cancellationToken = default)
    {
        var response = new ListMySubscriptionsResponse(request.CorrelationId());

        try
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(new { message = "User ID not found in token" });
            }

            var subscriptions = await _subscriptionService.GetUserSubscriptionsAsync(userId, cancellationToken);

            response.Subscriptions.AddRange(subscriptions.Select(s => new SubscriptionStateDto
            {
                Id = s.Id,
                CustomerId = s.CustomerId,
                ProductId = s.ProductId,
                State = s.State,
                ActivatedAt = s.ActivatedAt,
                CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
                CreatedAt = s.CreatedAt
            }));

            return Ok(response);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message, correlationId = response.CorrelationId() });
        }
    }
}
